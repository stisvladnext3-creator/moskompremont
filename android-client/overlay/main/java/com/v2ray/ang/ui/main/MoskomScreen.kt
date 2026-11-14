package com.v2ray.ang.ui.main

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.res.stringArrayResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import coil3.compose.AsyncImage
import com.v2ray.ang.BuildConfig
import com.v2ray.ang.R
import com.v2ray.ang.handler.moskom.MoskomPolicy

private val Ink=Color(0xFF090D1C)
private val CardInk=Color(0xFF151725)
private val Cyan=Color(0xFF64DFF4)
private val Muted=Color(0xFFA7B7D0)
private val Primary=Brush.linearGradient(listOf(Color(0xFF36D5E8),Color(0xFF7C8EFA),Color(0xFFA271F5)))
private val Background=Brush.linearGradient(listOf(Color(0xFF07070F),Color(0xFF111529),Color(0xFF160F27)))

@Composable
fun MoskomScreen(mainViewModel:MainViewModel,onAction:(MainAction)->Unit,onNavigate:(MainDestination)->Unit) {
    val controller=remember(mainViewModel){mainViewModel.moskom()}
    val state by controller.state.collectAsStateWithLifecycle()
    val native by mainViewModel.uiState.collectAsStateWithLifecycle()
    val servers by mainViewModel.serversForGroup(state.group).collectAsStateWithLifecycle()
    val action:(MoskomAction)->Unit={onAction(MainAction.Moskom(it))}
    var menu by rememberSaveable{mutableStateOf(false)}
    var dialog by rememberSaveable{mutableStateOf("")}
    val group=state.groups.firstOrNull{it.id==state.group}
    val server=group?.servers?.firstOrNull{it.id==state.server}
    val latency=servers.firstOrNull{it.guid==state.server}?.testDelayMillis
    val busy=state.busy||state.connecting||native.isTesting
    val addLabel=stringResource(R.string.moskom_add)
    val modes=listOf("all" to R.string.moskom_mode_all,"except_ru" to R.string.moskom_mode_except,"blocked" to R.string.moskom_mode_blocked)
    BackHandler(enabled=dialog.isNotEmpty()){dialog=""}
    MaterialTheme(colorScheme=darkColorScheme(primary=Cyan,onPrimary=Ink,surface=CardInk,onSurface=Color(0xFFF0F4FF),background=Ink,onBackground=Color.White)) {
        Scaffold(containerColor=Color.Transparent,contentWindowInsets=WindowInsets.safeDrawing) {padding->
            Box(Modifier.fillMaxSize().background(Background).padding(padding).consumeWindowInsets(padding),contentAlignment=Alignment.TopCenter) {
                Column(Modifier.widthIn(max=520.dp).fillMaxHeight().fillMaxWidth().verticalScroll(rememberScrollState()).imePadding().padding(horizontal=24.dp,vertical=22.dp),verticalArrangement=Arrangement.spacedBy(12.dp)) {
                    Row(Modifier.fillMaxWidth(),verticalAlignment=Alignment.CenterVertically) {
                        AsyncImage(model=R.drawable.moskom_logo,contentDescription=null,modifier=Modifier.size(54.dp))
                        Column(Modifier.weight(1f).padding(horizontal=10.dp)) {
                            Text(stringResource(R.string.moskom_brand),color=Cyan,fontSize=10.sp,fontWeight=FontWeight.Bold,letterSpacing=1.2.sp)
                            Text(stringResource(R.string.moskom_title),fontSize=22.sp,fontWeight=FontWeight.Bold)
                        }
                        Box {
                            val menuName=stringResource(R.string.moskom_menu)
                            OutlinedButton(onClick={menu=true},contentPadding=PaddingValues(10.dp),shape=RoundedCornerShape(10.dp),modifier=Modifier.size(48.dp).semantics{contentDescription=menuName}.testTag("moskom_menu")) {
                                Canvas(Modifier.size(25.dp)){for(y in listOf(.2f,.5f,.8f))drawLine(Cyan,Offset(1f,size.height*y),Offset(size.width-1,size.height*y),2.dp.toPx(),StrokeCap.Round)}
                            }
                            DropdownMenu(expanded=menu,onDismissRequest={menu=false}) {
                                MenuEntry(R.string.moskom_rules){menu=false;dialog="rules"}
                                MenuEntry(R.string.moskom_check_update){menu=false;dialog="updates";action(MoskomAction.CheckUpdate)}
                                MenuEntry(R.string.moskom_dns){menu=false;dialog="dns"}
                                HorizontalDivider()
                                MenuEntry(R.string.moskom_help){menu=false;dialog="help"}
                                MenuEntry(R.string.moskom_exit){menu=false;action(MoskomAction.Exit)}
                            }
                        }
                    }
                    Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                        Button(onClick={dialog="status";action(MoskomAction.Service)},shape=RoundedCornerShape(10.dp),colors=ButtonDefaults.buttonColors(containerColor=when{state.serviceFailed->Color(0xFF253147);state.notice.level=="ok"&&state.notice.enabled->Color(0xFF14694E);state.notice.level=="down"&&state.notice.enabled->Color(0xFF8B303D);else->Color(0xFF806017)},contentColor=Color.White),modifier=Modifier.weight(1f).heightIn(min=44.dp).testTag("moskom_status")) {
                            Text(stringResource(if(state.serviceFailed||!state.notice.enabled)R.string.moskom_status else when(state.notice.level){"ok"->R.string.moskom_status_ok;"down"->R.string.moskom_status_down;else->R.string.moskom_status_warning}),fontSize=11.sp,textAlign=TextAlign.Center)
                        }
                        OutlinedButton(onClick={dialog="account"},enabled=state.loaded&&!busy,shape=RoundedCornerShape(10.dp),modifier=Modifier.weight(1f).heightIn(min=44.dp).testTag("moskom_account")){Text(stringResource(if(state.user==null)R.string.moskom_login else R.string.moskom_cabinet),fontSize=12.sp)}
                    }
                    state.user?.let{Text(it.name,color=Muted,fontSize=12.sp)}
                    if(state.error!=0)Text(stringResource(state.error),color=Color(0xFFFFB0BB),fontSize=12.sp,modifier=Modifier.testTag("moskom_error"))
                    else if(state.message!=0)Text(stringResource(state.message),color=Cyan,fontSize=12.sp)
                    if(!state.loaded||state.busy)LinearProgressIndicator(Modifier.fillMaxWidth(),color=Cyan)
                    val powerLabel=stringResource(when{state.connecting->R.string.moskom_connecting;native.isRunning->R.string.moskom_power_off;else->R.string.moskom_power_on})
                    Box(Modifier.fillMaxWidth().padding(top=12.dp,bottom=2.dp),contentAlignment=Alignment.Center) {
                        Button(onClick={onAction(MainAction.ToggleService)},enabled=native.isRunning||(!busy&&state.loaded&&server!=null),shape=CircleShape,colors=ButtonDefaults.buttonColors(containerColor=Color.Transparent,disabledContainerColor=Color.Transparent,contentColor=Color.White),contentPadding=PaddingValues(0.dp),modifier=Modifier.size(154.dp).background(if(native.isRunning)Brush.linearGradient(listOf(Color(0xFF157D6A),Color(0xFF12696A)))else Brush.linearGradient(listOf(Color(0xFF087A9D),Color(0xFF3B5DD5),Color(0xFF7041C9))),CircleShape).testTag("moskom_power")) {
                            Column(horizontalAlignment=Alignment.CenterHorizontally,verticalArrangement=Arrangement.spacedBy(12.dp)) {
                                Canvas(Modifier.size(66.dp)){val stroke=Stroke(7.dp.toPx(),cap=StrokeCap.Round);drawArc(Color.White,135f,270f,false,Offset(size.width*.1f,size.height*.1f),Size(size.width*.8f,size.height*.8f),style=stroke);drawLine(Color.White,Offset(size.width/2,0f),Offset(size.width/2,size.height*.43f),7.dp.toPx(),StrokeCap.Round)}
                                Text(powerLabel,fontWeight=FontWeight.Bold,fontSize=13.sp)
                            }
                        }
                    }
                    Text(stringResource(when{state.connecting->R.string.moskom_connecting;native.isRunning->R.string.moskom_connected;else->R.string.moskom_disconnected}),fontSize=23.sp,fontWeight=FontWeight.Bold,textAlign=TextAlign.Center,modifier=Modifier.fillMaxWidth())
                    Text(stringResource(if(state.groups.isEmpty())R.string.moskom_empty else R.string.moskom_choose_hint),color=Muted,fontSize=13.sp,textAlign=TextAlign.Center,modifier=Modifier.fillMaxWidth().padding(bottom=12.dp))
                    Caption(R.string.moskom_subscription)
                    Row(horizontalArrangement=Arrangement.spacedBy(8.dp),verticalAlignment=Alignment.CenterVertically) {
                        Picker(stringResource(R.string.moskom_subscription),group?.let{stringResource(R.string.moskom_group_count,it.name,it.servers.size)}?:stringResource(R.string.moskom_choose_subscription),state.groups.map{it.id to stringResource(R.string.moskom_group_count,it.name,it.servers.size)},!busy&&state.groups.isNotEmpty(),{action(MoskomAction.Group(it))},Modifier.weight(1f),tag="moskom_subscription")
                        OutlinedButton(onClick={onAction(MainAction.ImportClipboard)},enabled=state.loaded&&!busy,shape=RoundedCornerShape(10.dp),contentPadding=PaddingValues(0.dp),modifier=Modifier.size(48.dp).testTag("moskom_add").semantics{contentDescription=addLabel}){Canvas(Modifier.size(20.dp)){drawLine(Cyan,Offset(size.width/2,0f),Offset(size.width/2,size.height),2.dp.toPx());drawLine(Cyan,Offset(0f,size.height/2),Offset(size.width,size.height/2),2.dp.toPx())}}
                    }
                    if(group!=null&&group.servers.isEmpty())Text(stringResource(R.string.moskom_no_servers),color=Muted,fontSize=12.sp)
                    Caption(R.string.moskom_server)
                    Picker(stringResource(R.string.moskom_server),server?.name?:stringResource(R.string.moskom_choose_server),group?.servers.orEmpty().map{it.id to it.name},!busy&&group?.servers?.isNotEmpty()==true,{action(MoskomAction.Server(it))},Modifier.fillMaxWidth(),tag="moskom_server")
                    Caption(R.string.moskom_routing)
                    Picker(stringResource(R.string.moskom_routing),stringResource(modes.firstOrNull{it.first==state.mode}?.second?:R.string.moskom_mode_all),modes.map{it.first to stringResource(it.second)},state.loaded&&!busy,{action(MoskomAction.Mode(it))},Modifier.fillMaxWidth(),tag="moskom_routing")
                    Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {
                        SmallButton(R.string.moskom_update_servers,state.loaded&&!busy,{action(MoskomAction.Sync)},Modifier.weight(1f).testTag("moskom_sync"))
                        SmallButton(if(native.isTesting)R.string.moskom_testing else R.string.moskom_availability,state.loaded&&!busy&&server!=null,{onAction(MainAction.MoskomAvailability)},Modifier.weight(1f).testTag("moskom_availability"))
                    }
                    Text(when{native.isTesting->stringResource(R.string.moskom_testing);latency!=null&&latency>0->stringResource(R.string.moskom_ms,latency);latency!=null&&latency<0->stringResource(R.string.moskom_unavailable);else->stringResource(R.string.moskom_unchecked)},color=if(latency!=null&&latency>0)Color(0xFF55D6AC)else Muted,fontSize=12.sp,textAlign=TextAlign.Center,modifier=Modifier.fillMaxWidth().testTag("moskom_delay"))
                    Text(stringResource(R.string.moskom_android_mode),color=Muted,fontSize=11.sp,textAlign=TextAlign.Center,modifier=Modifier.fillMaxWidth())
                    Row(horizontalArrangement=Arrangement.spacedBy(8.dp)) {SmallButton(R.string.moskom_help,true,{dialog="help"},Modifier.weight(1f).testTag("moskom_help"));SmallButton(R.string.moskom_exit,!busy,{action(MoskomAction.Exit)},Modifier.weight(1f))}
                    Text(stringResource(R.string.moskom_version,BuildConfig.VERSION_NAME),color=Muted,fontSize=10.sp,textAlign=TextAlign.Center,modifier=Modifier.fillMaxWidth().padding(bottom=10.dp))
                }
            }
        }
        val release=state.release
        if(release!=null) {
            Sheet(stringResource(R.string.moskom_update_title,release.version),onClose={if(!state.busy)action(MoskomAction.Later)}) {
                Text(stringResource(R.string.moskom_update_description))
                if(state.progress!=null)Text(stringResource(R.string.moskom_progress,state.progress?:0),color=Cyan)
                if(state.error!=0)Text(stringResource(state.error),color=Color(0xFFFFB0BB))
                PrimaryButton(if(state.updateFile!=null)R.string.moskom_install_update else R.string.moskom_update_now,!state.busy,{action(if(state.updateFile!=null)MoskomAction.InstallUpdate else MoskomAction.DownloadUpdate)},Modifier.testTag("moskom_update_now"))
                SmallButton(if(state.busy)R.string.moskom_cancel else R.string.moskom_later,true,{action(if(state.busy)MoskomAction.Cancel else MoskomAction.Later)},Modifier.fillMaxWidth())
            }
        } else when(dialog) {
            "help"->Sheet(stringResource(R.string.moskom_help),{dialog=""}) {
                val titles=stringArrayResource(R.array.moskom_help_titles);val bodies=stringArrayResource(R.array.moskom_help_bodies)
                titles.forEachIndexed { index,title->key(index){Surface(shape=RoundedCornerShape(12.dp),color=CardInk){Column(Modifier.padding(16.dp),verticalArrangement=Arrangement.spacedBy(6.dp)){Text(title,color=Cyan,fontWeight=FontWeight.Bold,fontSize=14.sp);Text(bodies[index],fontSize=13.sp,color=Muted)}}} }
            }
            "account"->AccountSheet(state,action){dialog=""}
            "dns"->DnsSheet(state,action){dialog=""}
            "rules"->Sheet(stringResource(R.string.moskom_rules),{dialog=""}){Text(stringResource(R.string.moskom_rules_description));Text(stringResource(R.string.moskom_rules_stamp,state.rulesStamp),color=Muted,fontSize=12.sp);OperationStatus(state);PrimaryButton(R.string.moskom_rules,!busy,{action(MoskomAction.Rules)})}
            "updates"->Sheet(stringResource(R.string.moskom_check_update),{dialog=""}){Text(stringResource(R.string.moskom_version,BuildConfig.VERSION_NAME));OperationStatus(state);PrimaryButton(R.string.moskom_check_update,!busy,{action(MoskomAction.CheckUpdate)})}
            "status"->Sheet(stringResource(R.string.moskom_status),{dialog=""}){
                if(state.notice.enabled)Text(state.notice.message,color=Color(0xFFDFE7F7))
                if(state.serviceFailed)Text(stringResource(R.string.moskom_service_error),color=Muted)
                state.nodes.forEach {node->key(node.name){Column(Modifier.fillMaxWidth().background(CardInk,RoundedCornerShape(10.dp)).padding(12.dp)){Text(node.name,fontWeight=FontWeight.Bold);Text(if(!MoskomPolicy.fresh(node.checked))stringResource(R.string.moskom_stale)else when(node.status){"up"->stringResource(R.string.moskom_available)+(node.ms?.takeIf{it>0}?.let{" · "+stringResource(R.string.moskom_ms,it)}?:"");"down"->stringResource(R.string.moskom_unavailable);else->stringResource(R.string.moskom_stale)},color=Muted,fontSize=13.sp)}}}
                SmallButton(R.string.moskom_refresh,true,{action(MoskomAction.Service)},Modifier.fillMaxWidth())
            }
        }
    }
}

@Composable private fun Caption(id:Int){Text(stringResource(id),color=Muted,fontSize=12.sp)}
@Composable private fun MenuEntry(id:Int,click:()->Unit){DropdownMenuItem(text={Text(stringResource(id))},onClick=click)}
@Composable private fun SmallButton(id:Int,enabled:Boolean,click:()->Unit,modifier:Modifier=Modifier){OutlinedButton(onClick=click,enabled=enabled,shape=RoundedCornerShape(10.dp),contentPadding=PaddingValues(horizontal=10.dp,vertical=11.dp),modifier=modifier.heightIn(min=44.dp)){Text(stringResource(id),fontSize=12.sp,textAlign=TextAlign.Center)}}
@Composable private fun PrimaryButton(id:Int,enabled:Boolean,click:()->Unit,modifier:Modifier=Modifier){Button(onClick=click,enabled=enabled,colors=ButtonDefaults.buttonColors(containerColor=Color.Transparent,contentColor=Ink),shape=RoundedCornerShape(10.dp),modifier=modifier.fillMaxWidth().background(Primary,RoundedCornerShape(10.dp)).heightIn(min=48.dp)){Text(stringResource(id),fontWeight=FontWeight.Bold)}}
@Composable private fun Picker(label:String,value:String,items:List<Pair<String,String>>,enabled:Boolean,choose:(String)->Unit,modifier:Modifier,tag:String=""){var expanded by rememberSaveable{mutableStateOf(false)};Box(modifier){OutlinedButton(onClick={expanded=true},enabled=enabled,shape=RoundedCornerShape(10.dp),contentPadding=PaddingValues(horizontal=13.dp,vertical=13.dp),modifier=Modifier.fillMaxWidth().then(if(tag.isEmpty())Modifier else Modifier.testTag(tag)).semantics{contentDescription=label}){Text(value,color=Color(0xFFEDF3FF),modifier=Modifier.weight(1f),fontSize=13.sp,maxLines=2);Canvas(Modifier.size(12.dp)){drawLine(Muted,Offset(0f,size.height*.3f),Offset(size.width/2,size.height*.7f),2.dp.toPx(),StrokeCap.Round);drawLine(Muted,Offset(size.width/2,size.height*.7f),Offset(size.width,size.height*.3f),2.dp.toPx(),StrokeCap.Round)}};DropdownMenu(expanded,onDismissRequest={expanded=false},modifier=Modifier.heightIn(max=300.dp)){items.forEach{(id,text)->key(id){DropdownMenuItem(text={Text(text)},onClick={expanded=false;choose(id)})}}}}}
@Composable private fun Sheet(title:String,onClose:()->Unit,body:@Composable ColumnScope.()->Unit){Dialog(onDismissRequest=onClose,properties=DialogProperties(usePlatformDefaultWidth=false)){Surface(modifier=Modifier.fillMaxWidth(.94f).heightIn(max=680.dp).imePadding().navigationBarsPadding(),shape=RoundedCornerShape(20.dp),color=Ink){Column(Modifier.verticalScroll(rememberScrollState()).padding(22.dp),verticalArrangement=Arrangement.spacedBy(14.dp)){Row(verticalAlignment=Alignment.CenterVertically){AsyncImage(R.drawable.moskom_logo,null,Modifier.size(42.dp));Text(title,fontSize=20.sp,fontWeight=FontWeight.Bold,modifier=Modifier.weight(1f).padding(start=10.dp))};body();SmallButton(R.string.moskom_close,true,onClose,Modifier.fillMaxWidth())}}}}
@Composable private fun OperationStatus(state:MoskomState){if(state.busy){LinearProgressIndicator(Modifier.fillMaxWidth());state.progress?.let{Text(stringResource(R.string.moskom_progress,it))}};if(state.error!=0)Text(stringResource(state.error),color=Color(0xFFFFB0BB))else if(state.message!=0)Text(stringResource(state.message),color=Cyan)}
@Composable private fun AccountSheet(state:MoskomState,action:(MoskomAction)->Unit,close:()->Unit){var email by rememberSaveable{mutableStateOf("")};var password by remember{mutableStateOf("")};LaunchedEffect(state.user){if(state.user!=null)password=""};Sheet(stringResource(R.string.moskom_cabinet),close){if(state.user==null){OutlinedTextField(value=email,onValueChange={email=it},label={Text(stringResource(R.string.moskom_email))},singleLine=true,keyboardOptions=KeyboardOptions(keyboardType=KeyboardType.Email,imeAction=ImeAction.Next),modifier=Modifier.fillMaxWidth().testTag("moskom_email"));OutlinedTextField(value=password,onValueChange={password=it},label={Text(stringResource(R.string.moskom_password))},singleLine=true,visualTransformation=PasswordVisualTransformation(),keyboardOptions=KeyboardOptions(keyboardType=KeyboardType.Password,imeAction=ImeAction.Done),keyboardActions=KeyboardActions(onDone={if(!state.busy)action(MoskomAction.Login(email,password))}),modifier=Modifier.fillMaxWidth().testTag("moskom_password"));PrimaryButton(R.string.moskom_login,!state.busy&&email.contains('@')&&password.isNotEmpty(),{action(MoskomAction.Login(email,password))},Modifier.testTag("moskom_login_submit"));Text(stringResource(R.string.moskom_login_description),fontSize=12.sp,color=Muted)}else{Text(state.user.name,fontWeight=FontWeight.Bold);Text(state.user.email,color=Muted);Text(stringResource(R.string.moskom_account_subscriptions,state.groups.count{it.id.startsWith(MoskomPolicy.CABINET)}));PrimaryButton(R.string.moskom_update_servers,!state.busy,{action(MoskomAction.Sync)});SmallButton(R.string.moskom_logout,!state.busy,{action(MoskomAction.Logout)},Modifier.fillMaxWidth())};OperationStatus(state)}}
@Composable private fun DnsSheet(state:MoskomState,action:(MoskomAction)->Unit,close:()->Unit){var dns by rememberSaveable(state.dns){mutableStateOf(state.dns)};var custom by rememberSaveable(state.customDns){mutableStateOf(state.customDns)};val options=listOf("auto" to R.string.moskom_dns_auto,"cloudflare" to R.string.moskom_dns_cloudflare,"google" to R.string.moskom_dns_google,"quad9" to R.string.moskom_dns_quad9,"custom" to R.string.moskom_dns_custom);Sheet(stringResource(R.string.moskom_dns),close){Text(stringResource(R.string.moskom_dns_description),color=Muted,fontSize=13.sp);Picker(stringResource(R.string.moskom_dns),stringResource(options.firstOrNull{it.first==dns}?.second?:R.string.moskom_dns_auto),options.map{it.first to stringResource(it.second)},!state.busy,{dns=it},Modifier.fillMaxWidth());if(dns=="custom")OutlinedTextField(custom,{custom=it},label={Text(stringResource(R.string.moskom_dns_custom_hint))},modifier=Modifier.fillMaxWidth());PrimaryButton(R.string.moskom_save_dns,!state.busy,{action(MoskomAction.Dns(dns,custom))});OperationStatus(state)}}
