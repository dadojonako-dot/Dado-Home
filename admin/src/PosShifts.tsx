import React,{useEffect,useRef,useState} from 'react';
import {adminApi} from './api';

type Shift={id:string;cashierName:string;cashierId:string;openedAt:string;closedAt:string|null;openingCash:number;countedCash:number|null;expectedCash:number|null;difference:number|null};
type Totals={shift:Shift;receipts:number;cash:number;card:number;qr:number;cashReturns:number;cardReturns:number;qrReturns:number;expectedCash:number};
const money=(value:number)=>`${value.toFixed(2)} с.`;
const base='/api/admin/pos/shifts';

export default function PosShifts({role,history,refresh,disabled,onCurrent,onBusy}:{role:string;history:boolean;refresh:number;disabled:boolean;onCurrent:(id:string|null)=>void;onBusy:(busy:boolean)=>void}){
  const canOperate=role==='Administrator'||role==='Cashier';
  const [current,setCurrent]=useState<Shift|null>(null),[list,setList]=useState<Shift[]>([]),[view,setView]=useState<Totals|null>(null);
  const [opening,setOpening]=useState('0'),[counted,setCounted]=useState(''),[error,setError]=useState(''),[busy,setBusy]=useState(false),[loaded,setLoaded]=useState(false),[page,setPage]=useState(1);
  const pending=useRef<{url:string;body:unknown}|null>(null),lock=useRef(false);
  async function load(){
    if(canOperate){const result=await adminApi.get(`${base}/current`);setCurrent(result.shift);onCurrent(result.shift?.id||null);}
    if(history)setList(await adminApi.get(`${base}?page=${page}`));
    setLoaded(true);
  }
  useEffect(()=>{load().catch(e=>{setError(e.message);setLoaded(false);});},[refresh,history,page]);
  async function inspect(id:string){try{setView(await adminApi.get(`${base}/${id}`));setCounted('');setError('');}catch(e:any){setError(e.message);}}
  async function mutate(url:string,body:unknown){
    if(lock.current)return;lock.current=true;setBusy(true);onBusy(true);setError('');
    pending.current??={url,body};
    try{const op=pending.current;const result:Shift=await adminApi.post(op.url,op.body);pending.current=null;await load();await inspect(result.id);}
    catch(e:any){if(e.status&&e.status<500)pending.current=null;setError(e.message);}
    finally{lock.current=false;setBusy(false);onBusy(pending.current!==null);}
  }
  const blocked=disabled||busy||pending.current!==null;
  const valid=(text:string)=>text.trim()!==''&&Number.isFinite(Number(text))&&Number(text)>=0&&Math.abs(Math.round(Number(text)*100)-Number(text)*100)<0.000001;
  const errors=<>{error&&<p className="error" role="alert">{error}</p>}{pending.current&&!busy&&<button onClick={()=>mutate(pending.current!.url,pending.current!.body)}>Повторить запрос смены</button>}</>;
  if(!canOperate&&!history)return null;
  return <section className="panel pos-shifts">
    {errors}
    {canOperate&&<div className="pos-shift-bar">{!loaded?<span>Проверка смены…</span>:current?<><span><b>Смена открыта</b> · {new Date(current.openedAt).toLocaleString('ru-RU')}</span><button disabled={blocked} onClick={()=>inspect(current.id)}>Итоги / закрыть смену</button></>:<><span>Смена закрыта. Откройте её перед продажей или возвратом.</span><label>Наличные на начало<input aria-label="Наличные на начало смены" disabled={blocked} type="number" min="0" step="0.01" value={opening} onChange={e=>setOpening(e.target.value)}/></label><button className="primary" disabled={blocked||!valid(opening)} onClick={()=>mutate(base,{id:crypto.randomUUID(),openingCash:Number(opening)})}>Открыть смену</button></>}</div>}
    {history&&<><h2>Смены</h2>{!list.length&&<p>Смен пока нет.</p>}<div className="pos-receipts">{list.map(s=><article key={s.id}><div><b>{s.cashierName} · {new Date(s.openedAt).toLocaleString('ru-RU')}</b><small>{s.closedAt?`Закрыта ${new Date(s.closedAt).toLocaleString('ru-RU')}`:'Открыта'}</small></div><span>На начало: {money(s.openingCash)}</span>{s.difference!==null&&<span>Расхождение: {money(s.difference)}</span>}<button disabled={blocked} onClick={()=>inspect(s.id)}>Итоги</button></article>)}</div><div className="pos-tabs"><button disabled={blocked||page===1} onClick={()=>setPage(page-1)}>Назад</button><span>Страница {page}</span><button disabled={blocked||list.length<50} onClick={()=>setPage(page+1)}>Далее</button></div></>}
    {view&&<div className="modal" role="dialog" aria-modal="true" aria-label="Итоги смены"><section className="productform pos-shift-report">{errors}<div className="toolbar"><h2>Итоги смены</h2><button className="close" aria-label="Закрыть итоги смены" disabled={blocked} onClick={()=>setView(null)}>×</button></div><p>{view.shift.cashierName} · Открыта: {new Date(view.shift.openedAt).toLocaleString('ru-RU')} · Чеков: {view.receipts}</p><p>Начальные наличные: <b>{money(view.shift.openingCash)}</b></p><div className="tablewrap"><table><thead><tr><th>Способ</th><th>Продажи</th><th>Возвраты</th><th>Итого</th></tr></thead><tbody>{[['Наличные',view.cash,view.cashReturns],['Карта',view.card,view.cardReturns],['QR',view.qr,view.qrReturns]].map(([label,sales,returns])=><tr key={label}><td>{label}</td><td>{money(Number(sales))}</td><td>{money(Number(returns))}</td><td>{money(Number(sales)-Number(returns))}</td></tr>)}</tbody></table></div><h3>Ожидаемые наличные: {money(view.expectedCash)}</h3><p className="hint">Начальная сумма + наличные продажи − наличные возвраты. Сдача уже учтена. Возврат старого чека входит в текущую смену.</p>{view.shift.closedAt?<><p>Фактически: {money(view.shift.countedCash!)} · Расхождение: <b>{money(view.shift.difference!)}</b></p><p>Закрыта: {new Date(view.shift.closedAt).toLocaleString('ru-RU')}</p></>:canOperate&&<><label>Фактические наличные в кассе<input disabled={blocked} aria-label="Фактические наличные" type="number" min="0" step="0.01" value={counted} onChange={e=>setCounted(e.target.value)}/></label>{valid(counted)&&<p>Расхождение: {money(Number(counted)-view.expectedCash)}</p>}<button className="primary" disabled={blocked||!valid(counted)} onClick={()=>{if(confirm('Закрыть смену с указанной фактической суммой?'))mutate(`${base}/${view.shift.id}/close`,{countedCash:Number(counted)});}}>Закрыть смену</button></>}</section></div>}
  </section>;
}


