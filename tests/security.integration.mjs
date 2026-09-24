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
try{
  assert.equal(await start('Production','DEV_ONLY_CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_32_CHARS_MIN'),false);
  assert.match(logs,/Demo keys are rejected/);await stop();
  assert.equal(await start(),true);
  assert.equal((await req('/swagger/index.html')).status,404);
  assert.equal((await req('/api/auth/otp/request',null,{phone:'+992900001111'})).status,503);
  assert.equal((await req('/api/auth/otp/verify',null,{phone:'+992900001111',code:'000000'})).status,503);
  let admin=(await login()).accessToken;
  let target=await ok('/api/admin/staff',admin,{name:'Session test',phone:randomUUID(),role:'Administrator',password});
  const original=(await login(target.phone)).accessToken;
  assert.equal((await req('/api/audit',original)).status,200);
  target=await ok(`/api/admin/staff/${target.id}`,admin,{...target,name:'Renamed',password:null},'PUT');
  assert.equal((await req('/api/audit',original)).status,200); // a display-name edit does not log the user out
  target=await ok(`/api/admin/staff/${target.id}`,admin,{...target,role:'Finance',password:null},'PUT');
  for(const endpoint of ['/api/admin/me','/api/audit','/api/orders','/api/admin/pos/receipts'])assert.equal((await req(endpoint,original)).status,401);
  let finance=(await login(target.phone)).accessToken;
  assert.equal((await req('/api/audit',finance)).status,403);assert.equal((await req('/api/admin/payments',finance)).status,200);
  const nextPassword=randomBytes(24).toString('base64url');
  target=await ok(`/api/admin/staff/${target.id}`,admin,{...target,password:nextPassword},'PUT');
  assert.equal((await req('/api/admin/payments',finance)).status,401);
  finance=(await login(target.phone,nextPassword)).accessToken;
  target=await ok(`/api/admin/staff/${target.id}`,admin,{...target,isActive:false,password:null},'PUT');
  assert.equal((await req('/api/admin/payments',finance)).status,401);
  target=await ok(`/api/admin/staff/${target.id}`,admin,{...target,isActive:true,password:null},'PUT');
  assert.equal((await req('/api/admin/payments',finance)).status,401); // reactivation must not resurrect the old token
  const locked=await ok('/api/admin/staff',admin,{name:'Lockout test',phone:randomUUID(),role:'Cashier',password});
  const failures=await Promise.all(Array.from({length:5},()=>req('/api/admin/auth/login',null,{phone:locked.phone,password:'incorrect'})));
  assert.ok(failures.every(r=>r.status===401));assert.equal((await req('/api/admin/auth/login',null,{phone:locked.phone,password})).status,401);
  await stop();assert.equal(await start(),true); // IP limiter resets; account lockout must persist
  admin=(await login()).accessToken;
  assert.equal((await req('/api/admin/auth/login',null,{phone:locked.phone,password})).status,401);
  await ok(`/api/admin/staff/${locked.id}`,admin,{...locked,password},'PUT');
  assert.equal((await login(locked.phone)).user.role,'Cashier');
  let rejected=false;
  for(let i=0;i<25;i++){
    const r=await req('/api/admin/auth/login',null,{phone:`unknown-${i}`,password:'incorrect'},'POST',{'X-Forwarded-For':`10.1.0.${i}`});
    if(r.status===429){assert.ok(r.headers.get('retry-after'));rejected=true;break;}
  }
  assert.equal(rejected,true); // spoofed forwarded IP headers do not bypass the limiter
  await stop();assert.equal(await start('Development'),true);
  const formatted='+992 (900) 001-222',canonical='+992900001222';
  const code=(await ok('/api/auth/otp/request',null,{phone:formatted})).devCode;
  assert.match(code,/^[0-9]{6}$/);
  assert.equal((await req('/api/auth/otp/request',null,{phone:canonical})).status,429);
  const verifies=await Promise.all([1,2].map(()=>req('/api/auth/otp/verify',null,{phone:canonical,code})));
  assert.deepEqual(verifies.map(r=>r.status).sort(),[200,401]);
  const customer=verifies.find(r=>r.status===200).data.accessToken;
  assert.equal((await req('/api/admin/staff',customer)).status,403);
  assert.equal((await req('/api/wallet',customer)).status,200);
  const phone2='+992900001333',code2=(await ok('/api/auth/otp/request',null,{phone:phone2})).devCode;
  const wrong=code2==='000000'?'111111':'000000';
  await Promise.all(Array.from({length:5},()=>req('/api/auth/otp/verify',null,{phone:phone2,code:wrong})));
  assert.equal((await req('/api/auth/otp/verify',null,{phone:phone2,code:code2})).status,401);
  assert.equal((await req('/api/auth/otp/request',null,{phone:'not-a-phone'})).status,400);
  console.log('PASS: configuration guards, global session revocation, role/password/activation changes, persistent concurrent lockout, IP throttling, production OTP fail-closed, normalized one-time OTP and customer RBAC.');
}catch(error){console.error(logs.slice(-8000));throw error;}finally{await stop();}
