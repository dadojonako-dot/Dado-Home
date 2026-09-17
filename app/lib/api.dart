import 'dart:convert';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;
class ApiClient{ApiClient._();static final instance=ApiClient._();static const baseUrl=String.fromEnvironment('API_URL',defaultValue:'http://10.0.2.2:5000');final storage=const FlutterSecureStorage();String? token;
Future<String?> requestOtp(String phone)async{final r=await http.post(Uri.parse('$baseUrl/api/auth/otp/request'),headers:{'Content-Type':'application/json'},body:jsonEncode({'phone':phone}));if(r.statusCode<200||r.statusCode>=300)throw Exception('Не удалось отправить OTP');final data=jsonDecode(r.body);return data['devCode']?.toString();}
Future<void> verifyOtp(String phone,String code)async{final r=await http.post(Uri.parse('$baseUrl/api/auth/otp/verify'),headers:{'Content-Type':'application/json'},body:jsonEncode({'phone':phone,'code':code}));if(r.statusCode!=200)throw Exception('Неверный OTP');final data=jsonDecode(r.body);token=data['accessToken'];await storage.write(key:'access_token',value:token);}
Future<List<dynamic>> products()async{final r=await http.get(Uri.parse('$baseUrl/api/products'));if(r.statusCode!=200)throw Exception('Ошибка загрузки товаров');return jsonDecode(r.body) as List<dynamic>;}
Future<List<dynamic>> myOrders()async{final r=await _get('/api/me/orders');if(r.statusCode!=200)throw Exception('Ошибка загрузки заказов');return jsonDecode(r.body) as List<dynamic>;}
Future<Map<String,dynamic>> createOrder(Map<String,dynamic> order)async{final r=await _post('/api/orders',order);if(r.statusCode!=201)throw Exception('Ошибка оформления заказа');return jsonDecode(r.body) as Map<String,dynamic>;}
Future<http.Response> _get(String path)async{token??=await storage.read(key:'access_token');return http.get(Uri.parse('$baseUrl$path'),headers:{if(token!=null)'Authorization':'Bearer $token'});}
Future<http.Response> _post(String path,Object body)async{token??=await storage.read(key:'access_token');return http.post(Uri.parse('$baseUrl$path'),headers:{'Content-Type':'application/json',if(token!=null)'Authorization':'Bearer $token'},body:jsonEncode(body));}}
