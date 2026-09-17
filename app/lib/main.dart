import 'package:flutter/material.dart';

void main() => runApp(const DadoHomeApp());

const dadoRed = Color(0xFFE30613);

class DadoHomeApp extends StatelessWidget {
  const DadoHomeApp({super.key});
  @override
  Widget build(BuildContext context) => MaterialApp(
    debugShowCheckedModeBanner: false,
    title: 'ДАДО Home',
    theme: ThemeData(colorScheme: ColorScheme.fromSeed(seedColor: dadoRed), useMaterial3: true),
    home: const LoginScreen(),
  );
}

class LoginScreen extends StatelessWidget {
  const LoginScreen({super.key});
  @override
  Widget build(BuildContext context) => Scaffold(body: SafeArea(child: Padding(
    padding: const EdgeInsets.all(24),
    child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      const Spacer(),
      const Icon(Icons.shopping_bag_rounded, size: 72, color: dadoRed),
      const Text('ДАДО', textAlign: TextAlign.center, style: TextStyle(fontSize: 42, fontWeight: FontWeight.w900, color: dadoRed)),
      const Text('Одежда и 1000 мелочей', textAlign: TextAlign.center),
      const Spacer(),
      const Text('Вход в аккаунт', style: TextStyle(fontSize: 26, fontWeight: FontWeight.bold)),
      const SizedBox(height: 12),
      const TextField(keyboardType: TextInputType.phone, decoration: InputDecoration(prefixText: '+992 ', labelText: 'Номер телефона', border: OutlineInputBorder())),
      const SizedBox(height: 12),
      FilledButton(onPressed: () => Navigator.push(context, MaterialPageRoute(builder: (_) => const OtpScreen())), child: const Text('Получить код')),
      const Spacer(),
    ]),
  )));
}

class OtpScreen extends StatelessWidget {
  const OtpScreen({super.key});
  @override
  Widget build(BuildContext context) => Scaffold(appBar: AppBar(), body: Padding(
    padding: const EdgeInsets.all(24),
    child: Column(crossAxisAlignment: CrossAxisAlignment.stretch, children: [
      const Icon(Icons.verified_user_outlined, size: 64, color: dadoRed),
      const SizedBox(height: 20),
      const Text('Двухуровневая авторизация', style: TextStyle(fontSize: 25, fontWeight: FontWeight.bold)),
      const SizedBox(height: 8),
      const Text('Введите одноразовый код подтверждения.'),
      const SizedBox(height: 18),
      const TextField(maxLength: 6, keyboardType: TextInputType.number, decoration: InputDecoration(labelText: 'Код OTP', border: OutlineInputBorder())),
      FilledButton(onPressed: () => Navigator.pushAndRemoveUntil(context, MaterialPageRoute(builder: (_) => const HomeShell()), (_) => false), child: const Text('Подтвердить')),
    ]),
  ));
}

class HomeShell extends StatefulWidget { const HomeShell({super.key}); @override State<HomeShell> createState() => _HomeShellState(); }
class _HomeShellState extends State<HomeShell> {
  int index = 0;
  final pages = const [HomePage(), CatalogPage(), BasicPage('Корзина'), BasicPage('Мои заказы'), ProfilePage()];
  @override Widget build(BuildContext context) => Scaffold(body: pages[index], bottomNavigationBar: NavigationBar(selectedIndex: index, onDestinationSelected: (v) => setState(() => index=v), destinations: const [
    NavigationDestination(icon: Icon(Icons.home_outlined), label:'Главная'), NavigationDestination(icon: Icon(Icons.grid_view), label:'Каталог'), NavigationDestination(icon: Icon(Icons.shopping_cart_outlined), label:'Корзина'), NavigationDestination(icon: Icon(Icons.receipt_long_outlined), label:'Заказы'), NavigationDestination(icon: Icon(Icons.person_outline), label:'Профиль')
  ]));
}

class HomePage extends StatelessWidget { const HomePage({super.key}); @override Widget build(BuildContext context) => SafeArea(child: ListView(padding: const EdgeInsets.all(16), children: const [
  Text('ДАДО', style: TextStyle(fontSize:30,fontWeight:FontWeight.w900,color:dadoRed)), SizedBox(height:12),
  TextField(decoration: InputDecoration(prefixIcon:Icon(Icons.search),hintText:'Поиск одежды и товаров…',border:OutlineInputBorder())), SizedBox(height:20),
  Text('Категории', style:TextStyle(fontSize:22,fontWeight:FontWeight.bold)), SizedBox(height:10),
  Wrap(spacing:8,runSpacing:8,children:[Chip(label:Text('Женская одежда')),Chip(label:Text('Мужская одежда')),Chip(label:Text('Детская одежда')),Chip(label:Text('Обувь')),Chip(label:Text('Аксессуары')),Chip(label:Text('Для дома')),Chip(label:Text('1000 мелочей'))])
])); }

class CatalogPage extends StatelessWidget { const CatalogPage({super.key}); @override Widget build(BuildContext context) => const BasicPage('Каталог товаров'); }
class ProfilePage extends StatelessWidget { const ProfilePage({super.key}); @override Widget build(BuildContext context) => SafeArea(child: ListView(padding: const EdgeInsets.all(16), children: const [
  Text('Личный кабинет',style:TextStyle(fontSize:28,fontWeight:FontWeight.bold)), SizedBox(height:16),
  Card(child:ListTile(leading:Icon(Icons.account_balance_wallet),title:Text('ДАДО Wallet'),subtitle:Text('Баланс и история операций'),trailing:Icon(Icons.chevron_right))),
  ListTile(leading:Icon(Icons.credit_card),title:Text('Способы оплаты и эквайринг')), ListTile(leading:Icon(Icons.card_giftcard),title:Text('Бонусы')), ListTile(leading:Icon(Icons.security),title:Text('Безопасность и 2FA')), ListTile(leading:Icon(Icons.location_on_outlined),title:Text('Адреса доставки')), ListTile(leading:Icon(Icons.favorite_border),title:Text('Избранное'))
])); }
class BasicPage extends StatelessWidget { final String title; const BasicPage(this.title,{super.key}); @override Widget build(BuildContext context) => SafeArea(child:Padding(padding:const EdgeInsets.all(20),child:Text(title,style:const TextStyle(fontSize:28,fontWeight:FontWeight.bold)))); }
