using ServiceLib.Services;
using Avalonia.Automation;
namespace v2rayN.Desktop.Views;
public static class MoskomSelectiveViews {
 public static Task ApplicationsAsync(Window owner)=>ShowAsync(owner,true);
 public static Task ServicesAsync(Window owner)=>ShowAsync(owner,false);
 private static async Task ShowAsync(Window owner,bool applications){
  var window=new Window{Title=applications?"Приложения · МосКомпРемонт":"Сервисы · МосКомпРемонт",Width=530,Height=650,MinWidth=400,MinHeight=400,WindowStartupLocation=WindowStartupLocation.CenterOwner,Background=MoskomBrand.Background,Icon=MoskomBrand.Icon()};
  MoskomBrand.StyleWindow(window);var root=new Grid{Margin=new(22),RowDefinitions=new("Auto,Auto,Auto,*,Auto"),RowSpacing=12};
  var heading=MoskomBrand.Heading(applications?"Приложения":"Сервисы");root.Children.Add(heading);
  var hint=new TextBlock{Text=applications?"Выберите запущенные приложения. Список и выбор хранятся только на этом устройстве. Для браузера могут понадобиться его сетевые помощники.":"Выберите сервисы для режима «Выбранные сервисы». Списки используют домены и адреса; общие серверы могут совпадать. Для полного охвата звонкового приложения выберите его в «Приложениях».",TextWrapping=TextWrapping.Wrap,Foreground=Brush.Parse("#A7B7D0"),FontSize=12};Grid.SetRow(hint,1);root.Children.Add(hint);
  var tools=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=8};var search=new TextBox{Name=applications?"MoskomAppsSearch":"MoskomServicesSearch",Watermark=applications?"Поиск приложений…":"Поиск сервисов…"};var refresh=new Button{Content="Обновить",IsVisible=applications};tools.Children.Add(search);Grid.SetColumn(refresh,1);tools.Children.Add(refresh);Grid.SetRow(tools,2);root.Children.Add(tools);
  var list=new StackPanel{Spacing=5};var scroll=new ScrollViewer{Name="MoskomChoicesScroll",Content=list};Grid.SetRow(scroll,3);root.Children.Add(scroll);
  var bottom=new Grid{ColumnDefinitions=new("*,Auto"),ColumnSpacing=8};var message=new TextBlock{Foreground=Brush.Parse("#A7B7D0"),TextWrapping=TextWrapping.Wrap,FontSize=12};var save=new Button{Name=applications?"MoskomSaveApps":"MoskomSaveServices",Content="Сохранить выбор",Background=MoskomBrand.Primary,Foreground=Brush.Parse("#070B15"),MinHeight=44};bottom.Children.Add(message);Grid.SetColumn(save,1);bottom.Children.Add(save);Grid.SetRow(bottom,4);root.Children.Add(bottom);window.Content=root;
  AutomationProperties.SetName(search,applications?"Поиск приложений":"Поиск сервисов");
  var selectedApps=MoskomLauncherRuntime.Preferences.Selective.Apps.ToDictionary(a=>a.Id,StringComparer.Ordinal);var selectedServices=MoskomLauncherRuntime.Preferences.Selective.Services.ToHashSet(StringComparer.Ordinal);
  List<MoskomAppChoice> choices=[];var closed=false;window.Closed+=(_,_)=>closed=true;
  void Draw(){list.Children.Clear();var filter=search.Text?.Trim()??"";
   var rows=applications?choices.Select(a=>(a.Id,a.Name,a.Path)):MoskomLauncherRuntime.ServiceCatalog.Services.Select(s=>(s.Id,s.Name,string.Join(", ",s.Domains.Select(d=>d[(d.IndexOf(':')+1)..]))));
   var visible=rows.Where(r=>r.Name.Contains(filter,StringComparison.CurrentCultureIgnoreCase)||r.Id.Contains(filter,StringComparison.OrdinalIgnoreCase)||r.Item3.Contains(filter,StringComparison.CurrentCultureIgnoreCase)).ToList();
   foreach(var row in visible){var check=new CheckBox{Name="MoskomChoice",Content=row.Name,IsChecked=applications?selectedApps.ContainsKey(row.Id):selectedServices.Contains(row.Id),Foreground=Brush.Parse("#E7EFFF"),MinHeight=40};AutomationProperties.SetName(check,row.Name);if(applications)ToolTip.SetTip(check,row.Item3);check.IsCheckedChanged+=(_,_)=>{if(applications){var app=choices.First(a=>a.Id==row.Id);if(check.IsChecked==true)selectedApps[row.Id]=app;else selectedApps.Remove(row.Id);}else{if(check.IsChecked==true)selectedServices.Add(row.Id);else selectedServices.Remove(row.Id);}message.Text="Выбрано: "+(applications?selectedApps.Count:selectedServices.Count);};list.Children.Add(check);}
   if(visible.Count==0)list.Children.Add(new TextBlock{Text="Ничего не найдено.",Foreground=Brush.Parse("#A7B7D0")});message.Text="Выбрано: "+(applications?selectedApps.Count:selectedServices.Count);
  }
  async Task Load(){refresh.IsEnabled=false;save.IsEnabled=false;message.Text="Загрузка списка…";try{var running=await MoskomLauncherRuntime.RunningApplicationsAsync();if(closed)return;choices=running.Concat(selectedApps.Values).DistinctBy(a=>a.Id).OrderBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase).ToList();Draw();}catch{message.Text="Не удалось получить список. Повторите обновление.";}finally{refresh.IsEnabled=save.IsEnabled=true;}}
  search.TextChanged+=(_,_)=>Draw();refresh.Click+=async(_,_)=>await Load();save.Click+=async(_,_)=>{save.IsEnabled=false;try{await MoskomLauncherRuntime.SaveSelectiveAsync(selectedApps.Values,selectedServices);window.Close();}catch(Exception e){message.Text=e.Message;}finally{save.IsEnabled=true;}};
  window.Opened+=async(_,_)=>{if(applications)await Load();else Draw();};await window.ShowDialog(owner);
 }
}
