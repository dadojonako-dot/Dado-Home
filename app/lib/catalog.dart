import 'package:flutter/material.dart';

const dadoRed = Color(0xFFE30613);

class Product {
  final String name, category, description;
  final double price;
  final List<String> sizes, colors;
  final IconData icon;
  const Product(this.name, this.category, this.price, this.description, this.sizes, this.colors, this.icon);
}

const products = [
  Product('Футболка Classic', 'Мужская одежда', 129, 'Базовая хлопковая футболка', ['S','M','L','XL'], ['Белый','Черный'], Icons.checkroom),
  Product('Платье City', 'Женская одежда', 249, 'Повседневное женское платье', ['S','M','L'], ['Черный','Бежевый'], Icons.checkroom),
  Product('Кроссовки Street', 'Обувь', 399, 'Универсальные городские кроссовки', ['39','40','41','42','43'], ['Белый','Черный'], Icons.directions_run),
  Product('Органайзер Home', '1000 мелочей', 49, 'Компактный органайзер для дома', ['Стандарт'], ['Белый','Серый'], Icons.inventory_2_outlined),
  Product('Сумка Daily', 'Аксессуары', 179, 'Практичная сумка на каждый день', ['Стандарт'], ['Черный','Коричневый'], Icons.shopping_bag_outlined),
  Product('Набор контейнеров', 'Для дома', 89, 'Набор контейнеров для хранения', ['3 шт.'], ['Прозрачный'], Icons.kitchen_outlined),
];

class CatalogPage extends StatefulWidget { const CatalogPage({super.key}); @override State<CatalogPage> createState()=>_CatalogPageState(); }
class _CatalogPageState extends State<CatalogPage> {
  String query=''; String category='Все';
  @override Widget build(BuildContext context) {
    final filtered=products.where((p)=>(category=='Все'||p.category==category)&&p.name.toLowerCase().contains(query.toLowerCase())).toList();
    return SafeArea(child: Column(children:[
      Padding(padding:const EdgeInsets.fromLTRB(16,16,16,8),child:Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
        const Text('Каталог',style:TextStyle(fontSize:28,fontWeight:FontWeight.bold)), const SizedBox(height:12),
        TextField(onChanged:(v)=>setState(()=>query=v),decoration:const InputDecoration(prefixIcon:Icon(Icons.search),hintText:'Найти товар',border:OutlineInputBorder())),
        const SizedBox(height:10), SizedBox(height:42,child:ListView(scrollDirection:Axis.horizontal,children:['Все','Женская одежда','Мужская одежда','Обувь','Аксессуары','Для дома','1000 мелочей'].map((c)=>Padding(padding:const EdgeInsets.only(right:7),child:ChoiceChip(label:Text(c),selected:category==c,onSelected:(_)=>setState(()=>category=c)))).toList()))
      ])),
      Expanded(child:GridView.builder(padding:const EdgeInsets.all(12),gridDelegate:const SliverGridDelegateWithFixedCrossAxisCount(crossAxisCount:2,childAspectRatio:.68,crossAxisSpacing:10,mainAxisSpacing:10),itemCount:filtered.length,itemBuilder:(context,i)=>ProductCard(product:filtered[i])))
    ]));
  }
}

class ProductCard extends StatelessWidget { final Product product; const ProductCard({super.key,required this.product});
 @override Widget build(BuildContext context)=>Card(clipBehavior:Clip.antiAlias,child:InkWell(onTap:()=>Navigator.push(context,MaterialPageRoute(builder:(_)=>ProductDetails(product:product))),child:Column(crossAxisAlignment:CrossAxisAlignment.start,children:[
  Expanded(child:Container(color:const Color(0xFFF1F1F1),alignment:Alignment.center,child:Icon(product.icon,size:72,color:Colors.black54))),
  Padding(padding:const EdgeInsets.all(10),child:Column(crossAxisAlignment:CrossAxisAlignment.start,children:[Text(product.name,maxLines:2,overflow:TextOverflow.ellipsis,style:const TextStyle(fontWeight:FontWeight.w600)),const SizedBox(height:5),Text('${product.price.toStringAsFixed(0)} с.',style:const TextStyle(fontSize:18,fontWeight:FontWeight.w800,color:dadoRed)),const SizedBox(height:7),SizedBox(width:double.infinity,child:FilledButton.icon(onPressed:(){ScaffoldMessenger.of(context).showSnackBar(SnackBar(content:Text('${product.name} добавлен в корзину')));},icon:const Icon(Icons.add_shopping_cart,size:18),label:const Text('В корзину')))]) )
 ])));
}

class ProductDetails extends StatefulWidget { final Product product; const ProductDetails({super.key,required this.product}); @override State<ProductDetails> createState()=>_ProductDetailsState(); }
class _ProductDetailsState extends State<ProductDetails>{ int size=0,color=0;
 @override Widget build(BuildContext context){final p=widget.product;return Scaffold(appBar:AppBar(title:Text(p.name)),body:ListView(padding:const EdgeInsets.all(18),children:[
  Container(height:260,decoration:BoxDecoration(color:const Color(0xFFF2F2F2),borderRadius:BorderRadius.circular(20)),child:Icon(p.icon,size:130,color:Colors.black54)),const SizedBox(height:18),
  Text(p.name,style:const TextStyle(fontSize:27,fontWeight:FontWeight.bold)),Text(p.category,style:const TextStyle(color:Colors.black54)),const SizedBox(height:8),Text('${p.price.toStringAsFixed(0)} сомони',style:const TextStyle(fontSize:25,fontWeight:FontWeight.w900,color:dadoRed)),const SizedBox(height:16),Text(p.description),const SizedBox(height:20),
  const Text('Размер',style:TextStyle(fontWeight:FontWeight.bold)),Wrap(spacing:7,children:List.generate(p.sizes.length,(i)=>ChoiceChip(label:Text(p.sizes[i]),selected:size==i,onSelected:(_)=>setState(()=>size=i)))),const SizedBox(height:14),
  const Text('Цвет',style:TextStyle(fontWeight:FontWeight.bold)),Wrap(spacing:7,children:List.generate(p.colors.length,(i)=>ChoiceChip(label:Text(p.colors[i]),selected:color==i,onSelected:(_)=>setState(()=>color=i)))),const SizedBox(height:24),
  FilledButton.icon(onPressed:(){ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content:Text('Товар добавлен в корзину')));},icon:const Icon(Icons.shopping_cart),label:const Padding(padding:EdgeInsets.all(14),child:Text('Добавить в корзину')))
 ]));}
}
