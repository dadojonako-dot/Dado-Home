import React, {useState} from 'react';
import './style.css';

type Role='Администратор'|'Менеджер заказов'|'Финансы'|'Служба поддержки';
const menus:Record<Role,string[]>={
 'Администратор':['Дашборд','Товары для дома','Заказы','Клиенты','Сотрудники','Настройки','Журнал аудита'],
 'Менеджер заказов':['Дашборд','Заказы','Доставка','Клиенты'],
 'Финансы':['Дашборд','Платежи','DADO Wallet','Возвраты','Бонусы','Отчеты'],
 'Служба поддержки':['Дашборд','Клиенты','Заказы','Обращения','История операций']
};
export default function App(){const[role,setRole]=useState<Role>('Администратор');const[section,setSection]=useState('Дашборд');return <div className="shell"><aside><div className="logo">ДАДО <small>HOME</small></div><select value={role} onChange={e=>{const r=e.target.value as Role;setRole(r);setSection('Дашборд')}}>{Object.keys(menus).map(r=><option>{r}</option>)}</select>{menus[role].map(m=><button className={section===m?'active':''} onClick={()=>setSection(m)}>{m}</button>)}</aside><main><header><div><h1>{section}</h1><p>{role}</p></div><div className="user">Админ ДАДО</div></header>{section==='Дашборд'?<Dashboard/>:<Module title={section} role={role}/>}</main></div>}
function Dashboard(){return <><div className="cards"><Card t="Продажи сегодня" v="12 480 с."/><Card t="Заказы" v="38"/><Card t="Средний чек" v="328 с."/><Card t="Новые клиенты" v="14"/></div><div className="panel"><h2>Последние заказы</h2><table><thead><tr><th>Заказ</th><th>Клиент</th><th>Сумма</th><th>Оплата</th><th>Статус</th></tr></thead><tbody><tr><td>#1048</td><td>Клиент 104</td><td>460 с.</td><td>DADO Wallet</td><td><b>Новый</b></td></tr><tr><td>#1047</td><td>Клиент 088</td><td>285 с.</td><td>Карта</td><td>Сборка</td></tr><tr><td>#1046</td><td>Клиент 215</td><td>710 с.</td><td>QR</td><td>Доставка</td></tr></tbody></table></div></>}
function Card({t,v}:{t:string,v:string}){return <div className="card"><span>{t}</span><strong>{v}</strong></div>}
function Module({title,role}:{title:string,role:Role}){return <div className="panel"><div className="toolbar"><div><h2>{title}</h2><p>Рабочий модуль: {role}</p></div>{role==='Администратор'&&title==='Товары для дома'?<button className="primary">+ Добавить товар</button>:null}</div><div className="empty">Модуль подключен к структуре панели. Следующий этап — API и PostgreSQL.</div></div>}
