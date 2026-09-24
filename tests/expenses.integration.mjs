// Disposable PostgreSQL only. Does not send SMS or contact external systems.
import assert from 'node:assert/strict';
import {randomUUID,randomBytes} from 'node:crypto';
import {spawn} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import path from 'node:path';
const connection=process.env.POS_TEST_CONNECTION;
if(!connection)throw new Error('Set POS_TEST_CONNECTION to a disposable empty PostgreSQL database.');
const project=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../backend/DadoHome.Api');
const origin='http://127.0.0.1:5190',password=randomBytes(24).toString('base64url'),phone=`owner-${randomUUID()}`,jwtKey=randomBytes(64).toString('base64');
let child,logs='';
async function stop(){if(child){child.kill();await new Promise(resolve=>{if(child.exitCode!==null)resolve();else child.once('exit',resolve);});child=null;}}
async function start(environment='Production',key=jwtKey){
  logs='';child=spawn(process.env.DOTNET||'dotnet',['run','--no-build','--no-launch-profile','--project',project],{
    env:{...process.env,ASPNETCORE_ENVIRONMENT:environment,ASPNETCORE_URLS:origin,ConnectionStrings__DadoDb:connection,Jwt__Key:key,Jwt__Issuer:'SecurityTests',Jwt__Audience:'SecurityTests',BootstrapAdmin__Phone:phone,BootstrapAdmin__Password:password},stdio:['ignore','pipe','pipe']});
  child.stdout.on('data',d=>logs+=d);child.stderr.on('data',d=>logs+=d);
  for(let i=0;i<120;i++){if(child.exitCode!==null)return false;try{if((await fetch(origin+'/health',{headers:{Connection:'close'}})).ok)return true;}catch{}await new Promise(r=>setTimeout(r,250));}
  throw new Error('Backend startup timeout');
}
async function req(url,token,body,method=body?'POST':'GET',headers={}){
  const response=await fetch(origin+url,{method,headers:{Connection:'close','Content-Type':'application/json',...(token?{Authorization:`Bearer ${token}`}:{ }),...headers},body:body?JSON.stringify(body):undefined});
  const text=await response.text();let data;try{data=JSON.parse(text);}catch{data=text;}return {status:response.status,data,headers:response.headers};
}
async function ok(url,token,body,method){const r=await req(url,token,body,method);assert.ok(r.status<300,`${url}: ${r.status}`);return r.data;}
const login=(p=phone,pw=password)=>ok('/api/admin/auth/login',null,{phone:p,password:pw});
try {
  assert.equal(await start(),true);
  const admin=(await login()).accessToken;
  const root='/api/admin/expenses';
  assert.equal((await req(root)).status,401);
  const entry={id:randomUUID(),date:'2026-09-25',amount:120.55,category:'Аренда',description:'Аренда магазина'};
  for(const role of ['Finance','Cashier','OrderManager','Support']) {
    const staff=await ok('/api/admin/staff',admin,{name:role,phone:randomUUID(),role,password});
    const token=(await login(staff.phone)).accessToken;
    assert.equal((await req(root,token)).status,role==='Finance'?200:403);
    assert.equal((await req(root,token,entry)).status,403);
    assert.equal((await req(root+'/'+entry.id+'/cancel',token,{reason:'test'})).status,403);
  }
  for(const change of [{amount:0},{amount:-1},{amount:1.001},{description:' '},{category:'invalid'},{id:'00000000-0000-0000-0000-000000000000'}])
    assert.equal((await req(root,admin,{...entry,...change})).status,400);
  const parallel=await Promise.all(Array.from({length:5},()=>req(root,admin,entry)));
  assert.equal(parallel.filter(r=>r.status===201).length,1);
  assert.ok(parallel.every(r=>r.status===200||r.status===201));
  assert.equal((await req(root,admin,{...entry,amount:999})).status,409);
  await ok(root,admin,{...entry,id:randomUUID(),date:'2026-08-01',amount:10,category:'Прочее'});
  let result=await ok(root+'?from=2026-09-01&to=2026-09-30',admin);
  assert.equal(result.count,1);assert.equal(result.total,120.55);
  assert.equal((await ok(root+'?category='+encodeURIComponent('Прочее'),admin)).total,10);
  assert.equal((await req(root+'?from=2026-10-01&to=2026-09-01',admin)).status,400);
  assert.equal((await req(root+'/'+entry.id+'/cancel',admin,{reason:''})).status,400);
  await Promise.all(Array.from({length:3},()=>ok(root+'/'+entry.id+'/cancel',admin,{reason:'Ошибочная запись'})));
  result=await ok(root+'?from=2026-09-01',admin);
  assert.equal(result.total,0);assert.equal(result.count,1);assert.equal(result.items[0].cancellationReason,'Ошибочная запись');
  const audit=await ok('/api/audit',admin);
  assert.equal(audit.filter(x=>x.entityId===entry.id&&x.action==='EXPENSE_CREATED').length,1);
  assert.equal(audit.filter(x=>x.entityId===entry.id&&x.action==='EXPENSE_CANCELLED').length,1);
  await stop();assert.equal(await start(),true);
  assert.equal((await ok(root,(await login()).accessToken)).total,10);
  console.log('PASS: expenses RBAC, validation, concurrent retries, filters, totals, cancellation, audit and persistence');
} finally {await stop();}
