import React, {useEffect, useRef, useState} from 'react';
import {adminApi} from './api';

const today = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`; };
const money = (n:number) => n.toLocaleString('ru-RU',{minimumFractionDigits:2,maximumFractionDigits:2})+' с.';
export default function Expenses({role}:{role:string}) {
  const [categories,setCategories]=useState<string[]>([]),[data,setData]=useState<any>(null),[error,setError]=useState('');
  const [from,setFrom]=useState(today().slice(0,7)+'-01'),[to,setTo]=useState(today()),[category,setCategory]=useState(''),[page,setPage]=useState(1);
  const [form,setForm]=useState<any>(null),[cancel,setCancel]=useState<any>(null),[reason,setReason]=useState(''),[busy,setBusy]=useState(false),[refresh,setRefresh]=useState(0);
  const request=useRef(0);
  useEffect(()=>{adminApi.get('/api/admin/expenses/categories').then(setCategories).catch(e=>setError(e.message));},[]);
  useEffect(()=>{
    const version=++request.current; setData(null);setError('');
    if(from&&to&&from>to){setError('Начало периода должно быть не позже окончания');return;}
    const params=new URLSearchParams({page:String(page)});if(from)params.set('from',from);if(to)params.set('to',to);if(category)params.set('category',category);
    adminApi.get('/api/admin/expenses?'+params).then(d=>{if(version===request.current)setData(d);}).catch(e=>{if(version===request.current)setError(e.message);});
    return()=>{request.current++;};
  },[from,to,category,page,refresh]);
  async function save(e:React.FormEvent){e.preventDefault();if(busy)return;setBusy(true);setError('');try{await adminApi.post('/api/admin/expenses',{...form,amount:Number(form.amount)});setForm(null);setRefresh(x=>x+1);}catch(e:any){setError(e.message);}finally{setBusy(false);}}
  async function cancelExpense(e:React.FormEvent){e.preventDefault();if(busy)return;setBusy(true);setError('');try{await adminApi.post(`/api/admin/expenses/${cancel.id}/cancel`,{reason});setCancel(null);setRefresh(x=>x+1);}catch(e:any){setError(e.message);}finally{setBusy(false);}}
  return <div className="panel expenses">
    <div className="toolbar"><div><h2>Расходы магазина</h2><p>Учёт затрат в сомони. Записи не изменяют наличные кассовой смены.</p></div>{role==='Administrator'&&<button className="primary" onClick={()=>{setError('');setForm({id:crypto.randomUUID(),date:today(),amount:'',category:categories[0]||'Аренда',description:''});}}>+ Добавить расход</button>}</div>
    <div className="formrow"><label>С даты<input type="date" value={from} onChange={e=>{setFrom(e.target.value);setPage(1);}}/></label><label>По дату<input type="date" value={to} onChange={e=>{setTo(e.target.value);setPage(1);}}/></label><label>Категория<select value={category} onChange={e=>{setCategory(e.target.value);setPage(1);}}><option value="">Все категории</option>{categories.map(c=><option key={c}>{c}</option>)}</select></label></div>
    {error&&!form&&!cancel&&<div className="error" role="alert">{error}</div>}
    {data?<><div className="cards"><div className="card"><span>Итого за выбранный период · без отменённых</span><strong>{money(data.total)}</strong></div><div className="card"><span>Записей по фильтру</span><strong>{data.count}</strong></div></div>
    <div className="tablewrap"><table><thead><tr><th>Дата</th><th>Категория</th><th>Описание</th><th>Сумма</th><th>Статус</th><th></th></tr></thead><tbody>{data.items.map((x:any)=><tr key={x.id}><td>{x.date.split('-').reverse().join('.')}</td><td>{x.category}</td><td style={{whiteSpace:'pre-wrap',overflowWrap:'anywhere',maxWidth:400}}>{x.description}{x.cancelledAt&&<small className="sub">Причина отмены: {x.cancellationReason}</small>}</td><td>{money(x.amount)}</td><td>{x.cancelledAt?'Отменён':'Учтён'}</td><td>{role==='Administrator'&&!x.cancelledAt&&<button className="link danger" onClick={()=>{setError('');setReason('');setCancel(x);}}>Отменить</button>}</td></tr>)}</tbody></table></div>
    {!data.items.length&&<div className="empty">За выбранный период расходов нет</div>}
    <div className="actions"><button disabled={page===1} onClick={()=>setPage(p=>p-1)}>Назад</button><span>Страница {page} из {Math.max(1,Math.ceil(data.count/50))}</span><button disabled={page*50>=data.count} onClick={()=>setPage(p=>p+1)}>Далее</button></div></>:!error&&<p>Загрузка расходов…</p>}
    {form&&<div className="modal"><form className="productform" onSubmit={save}><h2>Новый расход</h2><div className="formrow"><label>Дата<input required type="date" value={form.date} onChange={e=>setForm({...form,date:e.target.value})}/></label><label>Сумма, сомони<input required type="number" min="0.01" max="999999999999" step="0.01" value={form.amount} onChange={e=>setForm({...form,amount:e.target.value})}/></label></div><label>Категория<select value={form.category} onChange={e=>setForm({...form,category:e.target.value})}>{categories.map(c=><option key={c}>{c}</option>)}</select></label><label>Описание<textarea required maxLength={1000} rows={3} placeholder="Например, аренда помещения за сентябрь" value={form.description} onChange={e=>setForm({...form,description:e.target.value})}/></label>{error&&<div className="error" role="alert">{error}</div>}<div className="actions"><button type="button" disabled={busy} onClick={()=>setForm(null)}>Закрыть</button><button className="primary" disabled={busy}>{busy?'Сохранение…':'Сохранить'}</button></div></form></div>}
    {cancel&&<div className="modal"><form className="productform" onSubmit={cancelExpense}><h2>Отмена расхода {money(cancel.amount)}</h2><p>Запись сохранится в истории и перестанет учитываться в итогах. Для исправления внесите новый расход.</p><label>Причина<textarea required maxLength={500} value={reason} onChange={e=>setReason(e.target.value)}/></label>{error&&<div className="error" role="alert">{error}</div>}<div className="actions"><button type="button" disabled={busy} onClick={()=>setCancel(null)}>Закрыть</button><button className="primary" disabled={busy}>Подтвердить отмену</button></div></form></div>}
  </div>;
}
