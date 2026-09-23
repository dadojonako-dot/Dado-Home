import React, {useEffect, useRef, useState} from 'react';
import {adminApi} from './api';
import './pos.css';

type Product = {id:string; name:string; category:string; price:number; stock:number};
type Line = {id:string; productId:string; productName:string; quantity:number; returnedQuantity:number; unitPrice:number};
type Receipt = {id:string; number:string; status:string; total:number; tendered:number; paymentMethod:string; createdAt:string; lines:Line[]};
const base='/api/admin/pos';
const money=(v:number)=>`${v.toFixed(2)} с.`;
const status:Record<string,string>={Held:'Отложен',Sold:'Продан',Returned:'Возвращён',PartiallyReturned:'Частичный возврат',Cancelled:'Отменён'};
const methods:Record<string,string>={Cash:'Наличные',Card:'Карта',QR:'QR'};

export default function Pos({role}:{role:string}) {
  const canSell=role==='Administrator'||role==='Cashier';
  const [tab,setTab]=useState(canSell?'sale':'receipts');
  const [products,setProducts]=useState<Product[]>([]),[query,setQuery]=useState(''),[category,setCategory]=useState('');
  const [cart,setCart]=useState<{productId:string;quantity:number}[]>([]),[heldId,setHeldId]=useState<string|null>(null);
  const [method,setMethod]=useState('Cash'),[tendered,setTendered]=useState('');
  const [receipts,setReceipts]=useState<Receipt[]>([]),[page,setPage]=useState(1),[detail,setDetail]=useState<{receipt:Receipt;returns:any[]}|null>(null);
  const [report,setReport]=useState<any>(null),[day,setDay]=useState(new Date().toISOString().slice(0,10));
  const [error,setError]=useState(''),[busy,setBusy]=useState(false),[notice,setNotice]=useState('');
  const [returnLine,setReturnLine]=useState(''),[returnQty,setReturnQty]=useState(1);
  const pending=useRef<{path:string;body:any}|null>(null),lock=useRef(false);
  const total=cart.reduce((sum,x)=>sum+(products.find(p=>p.id===x.productId)?.price||0)*x.quantity,0);
  const disabled=busy||pending.current!==null;
  async function loadProducts(){setProducts(await adminApi.get('/api/products'));}
  async function loadReceipts(){setReceipts(await adminApi.get(`${base}/receipts?page=${page}${tab==='held'?'&status=Held':''}`));}
  useEffect(()=>{loadProducts().catch(e=>setError(e.message));},[]);
  useEffect(()=>{if(tab==='receipts'||tab==='held')loadReceipts().catch(e=>setError(e.message));},[tab,page]);
  useEffect(()=>{if(tab==='report'&&day){const start=new Date(`${day}T00:00:00Z`),end=new Date(start.getTime()+86400000);adminApi.get(`${base}/report?from=${start.toISOString()}&to=${end.toISOString()}`).then(setReport).catch(e=>setError(e.message));}},[tab,day]);
  function add(p:Product){if(disabled)return;setCart(old=>{const line=old.find(x=>x.productId===p.id);return line?old.map(x=>x.productId===p.id?{...x,quantity:Math.min(x.quantity+1,p.stock,1000)}:x):[...old,{productId:p.id,quantity:1}];});}
  async function open(id:string){setDetail(await adminApi.get(`${base}/receipts/${id}`));setReturnLine('');setReturnQty(1);}
  async function perform(path:string,body:any){
    if(lock.current)return;lock.current=true;setBusy(true);setError('');setNotice('');
    pending.current??={path,body:{...body,operationId:crypto.randomUUID()}};
    try {
      const op=pending.current;const receipt:Receipt=await adminApi.post(op.path,op.body);
      pending.current=null;
      if(op.path.endsWith('/sales')||op.path.endsWith('/held')){setCart([]);setHeldId(null);setTendered('');}
      setNotice(receipt.status==='Held'?'Чек отложен. Остатки не зарезервированы.':'Операция сохранена');
      if(receipt.status!=='Held')await open(receipt.id);
      await loadProducts();if(tab==='receipts'||tab==='held')await loadReceipts();
    }catch(e:any){if(e.status&&e.status<500)pending.current=null;setError(e.message);}
    finally{lock.current=false;setBusy(false);}
  }
  async function resume(r:Receipt){try{const d=await adminApi.get(`${base}/receipts/${r.id}`);setCart(d.receipt.lines.map((x:Line)=>({productId:x.productId,quantity:x.quantity})));setHeldId(r.id);setMethod(d.receipt.paymentMethod);setTendered('');setTab('sale');await loadProducts();setNotice('Цены и наличие будут проверены при продаже.');}catch(e:any){setError(e.message);}}
  async function cancel(r:Receipt){if(!confirm('Отменить отложенный чек?'))return;setBusy(true);try{await adminApi.delete(`${base}/held/${r.id}`);await loadReceipts();}catch(e:any){setError(e.message);}finally{setBusy(false);}}
  function checkout(held:boolean){perform(`${base}/${held?'held':'sales'}`,{items:cart,paymentMethod:method,tendered:held?0:method==='Cash'?Number(tendered):Number(total.toFixed(2)),heldReceiptId:heldId});}
  return <div className="pos">
    <nav className="pos-tabs" aria-label="Разделы кассы">{(canSell?['sale','held','receipts',...(role==='Administrator'?['report']:[])]:['receipts','report']).map(t=><button disabled={disabled} className={tab===t?'primary':''} key={t} onClick={()=>{setTab(t);setPage(1);setError('');}}>{({sale:'Продажа',held:'Отложенные чеки',receipts:'Чеки',report:'Отчёт'}as any)[t]}</button>)}</nav>
    {error&&<div role="alert" className="error">{error}</div>}{notice&&<p role="status">{notice}</p>}
    {pending.current&&!busy&&<div className="panel"><p>Ответ сервера не получен. Повторите ту же операцию, чтобы уточнить результат без повторного списания.</p><button onClick={()=>perform(pending.current!.path,pending.current!.body)}>Повторить операцию</button></div>}
    {tab==='sale'&&canSell&&<div className="pos-grid"><section className="panel"><h2>Товары</h2><div className="pos-search"><input aria-label="Поиск товара" placeholder="Название товара" value={query} onChange={e=>setQuery(e.target.value)}/><select aria-label="Категория" value={category} onChange={e=>setCategory(e.target.value)}><option value="">Все категории</option>{[...new Set(products.map(p=>p.category))].map(c=><option key={c}>{c}</option>)}</select></div><div className="pos-products">{products.filter(p=>p.name.toLowerCase().includes(query.toLowerCase())&&(!category||p.category===category)).map(p=><button disabled={disabled||p.stock<1} key={p.id} onClick={()=>add(p)}><b>{p.name}</b><span>{money(p.price)}</span><small>Остаток: {p.stock} шт.</small></button>)}</div></section>
    <section className="panel pos-cart"><h2>{heldId?'Отложенный чек':'Новый чек'}</h2>{cart.length===0&&<p className="empty">Выберите товары из каталога</p>}{cart.map(x=>{const p=products.find(p=>p.id===x.productId);return <div className="pos-line" key={x.productId}><b>{p?.name||'Товар недоступен'}</b><span>{money((p?.price||0)*x.quantity)}</span><input aria-label={`Количество ${p?.name||''}`} disabled={disabled} type="number" min="1" max="1000" value={x.quantity} onChange={e=>setCart(cart.map(i=>i===x?{...i,quantity:Number(e.target.value)}:i))}/><button disabled={disabled} onClick={()=>setCart(cart.filter(i=>i!==x))}>Удалить</button></div>;})}<div className="pos-total"><span>Итого</span><strong>{money(total)}</strong></div><label>Способ оплаты<select disabled={disabled} value={method} onChange={e=>setMethod(e.target.value)}>{Object.entries(methods).map(([v,n])=><option key={v} value={v}>{n}</option>)}</select></label>{method==='Cash'?<><label>Получено, сомони<input disabled={disabled} type="number" min="0" step="0.01" value={tendered} onChange={e=>setTendered(e.target.value)}/></label><p>Сдача: {money(Math.max(0,Number(tendered)-total))}</p></>:<p className="hint">Проводите продажу после подтверждения оплаты на терминале. Данные карты не вводятся.</p>}<button className="primary" disabled={disabled||!cart.length||cart.some(x=>!Number.isInteger(x.quantity)||x.quantity<1)||method==='Cash'&&Number(tendered)<total} onClick={()=>checkout(false)}>Провести продажу · {money(total)}</button><button disabled={disabled||!cart.length} onClick={()=>checkout(true)}>Отложить чек</button><button disabled={disabled} onClick={()=>{setCart([]);setHeldId(null);setTendered('');}}>Новый чек</button></section></div>}
    {(tab==='receipts'||tab==='held')&&<section className="panel"><h2>{tab==='held'?'Отложенные чеки':'История чеков'}</h2>{!receipts.length&&<p>Чеков пока нет</p>}<div className="pos-receipts">{receipts.map(r=><article key={r.id}><div><b>{r.number}</b><small>{new Date(r.createdAt).toLocaleString('ru-RU')} · {status[r.status]}</small></div><strong>{money(r.total)}</strong><button disabled={disabled} onClick={()=>open(r.id).catch(e=>setError(e.message))}>Открыть</button>{canSell&&r.status==='Held'&&<><button disabled={disabled||cart.length>0} onClick={()=>resume(r)}>Продолжить</button><button disabled={disabled} onClick={()=>cancel(r)}>Отменить</button></>}</article>)}</div><div className="pos-tabs"><button disabled={disabled||page===1} onClick={()=>setPage(page-1)}>Назад</button><span>Страница {page}</span><button disabled={disabled||receipts.length<50} onClick={()=>setPage(page+1)}>Далее</button></div></section>}
    {tab==='report'&&<section className="panel"><h2>Кассовый отчёт</h2><label>Дата (UTC)<input type="date" value={day} onChange={e=>setDay(e.target.value)}/></label>{report&&<div className="cards">{[['Чеков',report.count],['Продажи',money(report.gross)],['Возвраты',money(report.returned)],['Итого',money(report.net)]].map(([label,value])=><div className="card" key={label}><span>{label}</span><strong>{value}</strong></div>)}</div>}</section>}
    {detail&&<div className="modal" role="dialog" aria-modal="true" aria-label="Чек"><section className="productform pos-receipt">{error&&<div role="alert" className="error no-print">{error}</div>}{pending.current&&!busy&&<button className="no-print" onClick={()=>perform(pending.current!.path,pending.current!.body)}>Повторить операцию</button>}<div className="toolbar"><h2>ДАДО HOME</h2><button disabled={disabled} className="close no-print" aria-label="Закрыть чек" onClick={()=>setDetail(null)}>×</button></div><p className="pos-number">{detail.receipt.number}</p><p>{status[detail.receipt.status]} · {methods[detail.receipt.paymentMethod]}</p>{detail.receipt.lines.map(l=><div className="pos-line" key={l.id}><b>{l.productName}</b><span>{l.quantity} × {money(l.unitPrice)}</span><small>Возвращено: {l.returnedQuantity}</small></div>)}<h3>Итого: {money(detail.receipt.total)}</h3>{detail.receipt.status!=='Held'&&<p>Получено: {money(detail.receipt.tendered)} · Сдача: {money(detail.receipt.tendered-detail.receipt.total)}</p>}<p className="hint">Товарный чек. Подключение фискального регистратора не настроено.</p><button className="no-print" onClick={()=>window.print()}>Печать</button>{detail.returns.map(r=><p key={r.id}>Возврат {new Date(r.createdAt).toLocaleString('ru-RU')}: {r.quantity} шт. · {money(r.amount)}</p>)}{canSell&&['Sold','PartiallyReturned'].includes(detail.receipt.status)&&<div className="no-print"><h3>Возврат по чеку</h3><select aria-label="Товар для возврата" disabled={disabled} value={returnLine} onChange={e=>{setReturnLine(e.target.value);setReturnQty(1);}}><option value="">Выберите товар</option>{detail.receipt.lines.filter(l=>l.quantity>l.returnedQuantity).map(l=><option key={l.id} value={l.id}>{l.productName} (доступно {l.quantity-l.returnedQuantity})</option>)}</select><input aria-label="Количество к возврату" disabled={disabled} type="number" min="1" max={detail.receipt.lines.find(l=>l.id===returnLine)?.quantity||1} value={returnQty} onChange={e=>setReturnQty(Number(e.target.value))}/><p className="hint">Подтвердите фактическую выдачу денег / возврат через терминал перед записью возврата.</p><button disabled={disabled||!returnLine||!Number.isInteger(returnQty)||returnQty<1} onClick={()=>perform(`${base}/receipts/${detail.receipt.id}/returns`,{lineId:returnLine,quantity:returnQty})}>Оформить возврат</button></div>}</section></div>}
  </div>;
}

