package com.v2ray.ang.ui.main

import android.content.Context
import com.v2ray.ang.AppConfig
import com.v2ray.ang.R
import com.v2ray.ang.core.LauncherManager
import com.v2ray.ang.handler.moskom.*
import com.v2ray.ang.helper.MessageHelper
import com.v2ray.ang.util.LogUtil
import kotlinx.coroutines.*
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.flow.*
import kotlinx.coroutines.sync.Mutex
import kotlin.coroutines.resume

data class MoskomState(
    val loaded: Boolean=false,val busy:Boolean=false,val connecting:Boolean=false,
    val groups:List<MoskomGroup> = emptyList(),val group:String="",val server:String="",
    val user:MoskomUser?=null,val notice:MoskomNotice=MoskomNotice(),val nodes:List<MoskomNode> = emptyList(),
    val serviceFailed:Boolean=false,val mode:String="all",val dns:String="auto",val customDns:String="",val rulesStamp:String="",
    val message:Int=0,val error:Int=0,val progress:Int?=null,val release:MoskomRelease?=null,val updateFile:String?=null,
)
sealed interface MoskomAction {
    data class Import(val text:String):MoskomAction
    data class Group(val id:String):MoskomAction
    data class Server(val id:String):MoskomAction
    data class Login(val email:String,val password:String):MoskomAction
    data class Mode(val id:String):MoskomAction
    data class Dns(val id:String,val custom:String):MoskomAction
    data object Sync:MoskomAction
    data object Logout:MoskomAction
    data object Service:MoskomAction
    data object Rules:MoskomAction
    data object CheckUpdate:MoskomAction
    data object DownloadUpdate:MoskomAction
    data object InstallUpdate:MoskomAction
    data object Later:MoskomAction
    data object Cancel:MoskomAction
    data object Exit:MoskomAction
    data object Connecting:MoskomAction
    data object PermissionCancelled:MoskomAction
}
sealed interface MoskomEvent {
    data object Start:MoskomEvent
    data class Install(val path:String):MoskomEvent
    data object Exit:MoskomEvent
}

/** Owns the branded account/routing/update feature for the lifetime of MainViewModel. */
class MoskomMainController(
    private val context:Context,parent:CoroutineScope,
    private val native:StateFlow<MainUiState>,private val refresh:suspend (String)->Unit,
) {
    private val ownerJob=SupervisorJob(parent.coroutineContext[Job])
    private val scope=CoroutineScope(parent.coroutineContext+ownerJob)
    private val mutex=Mutex()
    private val _state=MutableStateFlow(MoskomState())
    val state:StateFlow<MoskomState> = _state.asStateFlow()
    private val eventChannel=Channel<MoskomEvent>(Channel.BUFFERED)
    val events=eventChannel.receiveAsFlow()
    @Volatile private var repository:MoskomRepository?=null
    private var operation:Job?=null
    private var connectionTimeout:Job?=null
    private val daemon=MutableStateFlow(0L to false)

    init {
        scope.launch {
            mutex.lock();_state.update{it.copy(busy=true)}
            try {
                withContext(Dispatchers.IO){MoskomRepository(context).also { repository=it;it.initialize() }}
                catalog();_state.update {it.copy(loaded=true)}
                service()
                sync(false,false)
            }catch(cancelled:CancellationException){throw cancelled}
            catch(error:Exception){failure(error);_state.update {it.copy(loaded=true)}}
            finally{mutex.unlock();_state.update{it.copy(busy=false)}}
            launch {delay(10000);checkUpdate(false);while(isActive){delay(6*60*60*1000L);checkUpdate(false)}}
            while(isActive){delay(60000);if(!native.value.isTesting&&mutex.tryLock()){
                _state.update{it.copy(busy=true)}
                try{service();sync(false,false)}finally{mutex.unlock();_state.update{it.copy(busy=false)}}
            }}
        }
    }
    private fun repo()=repository?:error("Repository not initialized")
    private suspend fun catalog() {
        val data=withContext(Dispatchers.IO){val r=repo();r.ensureSelection();MoskomState(groups=r.groups(),group=r.selectedGroup(),server=r.selectedServer(),user=r.user,mode=r.routes.mode(),dns=r.routes.dns(),customDns=r.routes.customDns(),rulesStamp=r.routes.stamp())}
        _state.update {it.copy(groups=data.groups,group=data.group,server=data.server,user=data.user,mode=data.mode,dns=data.dns,customDns=data.customDns,rulesStamp=data.rulesStamp)}
        refresh(data.group)
    }
    fun onNativeState(running:Boolean) {
        daemon.update { (it.first+1) to running };connectionTimeout?.cancel();_state.update {it.copy(connecting=false)}
    }
    fun onAction(action:MoskomAction) {
        when(action) {
            MoskomAction.Connecting->{
                _state.update{it.copy(connecting=true,error=0)};connectionTimeout?.cancel()
                connectionTimeout=scope.launch {delay(30000);_state.update{it.copy(connecting=false,error=R.string.moskom_connect_error)}}
            }
            MoskomAction.PermissionCancelled->{connectionTimeout?.cancel();_state.update{it.copy(connecting=false)}}
            MoskomAction.Cancel->{repository?.http?.cancel();operation?.cancel();_state.update{it.copy(busy=false,progress=null,message=R.string.moskom_cancelled)}}
            MoskomAction.Later->execute { _state.value.release?.let { release->withContext(Dispatchers.IO){repo().defer(release.version)} };_state.update{it.copy(release=null,updateFile=null)} }
            MoskomAction.Service->scope.launch {service()}
            MoskomAction.CheckUpdate->execute {checkUpdate(true)}
            MoskomAction.DownloadUpdate->execute {
                val release=_state.value.release?:return@execute
                val file=withContext(Dispatchers.IO){repo().downloadUpdate(release){value->_state.update{it.copy(progress=value)}}}
                _state.update{it.copy(updateFile=file.absolutePath,progress=null,message=R.string.moskom_update_ready)}
            }
            MoskomAction.InstallUpdate->execute {val path=_state.value.updateFile?:return@execute;stop();eventChannel.send(MoskomEvent.Install(path))}
            MoskomAction.Exit->execute {stop();eventChannel.send(MoskomEvent.Exit)}
            is MoskomAction.Login->execute {
                withContext(Dispatchers.IO){repo().login(action.email,action.password)}
                val failures=sync(true,true);catalog()
                if(failures==0)_state.update{it.copy(message=R.string.moskom_login_success)}
            }
            MoskomAction.Logout->execute {
                if(_state.value.group.startsWith(MoskomPolicy.CABINET))stop()
                withContext(Dispatchers.IO){repo().logout()};catalog();_state.update{it.copy(message=R.string.moskom_logout_success)}
            }
            MoskomAction.Sync->execute {
                val cabinetFailures=sync(true,false)
                val failed=withContext(Dispatchers.IO){repo().updateManual()};catalog()
                if(native.value.isRunning)LauncherManager.restartService(context)
                _state.update{it.copy(message=if(failed==0&&cabinetFailures==0)R.string.moskom_synced else R.string.moskom_partial_sync)}
            }
            is MoskomAction.Import->execute {
                val running=native.value.isRunning;if(running)stop()
                withContext(Dispatchers.IO){repo().importClipboard(action.text)};catalog()
                _state.update{it.copy(message=R.string.moskom_imported)};if(running&&_state.value.server.isNotBlank())eventChannel.send(MoskomEvent.Start)
            }
            is MoskomAction.Group->execute {changeSelection{repo().chooseGroup(action.id)}}
            is MoskomAction.Server->execute {changeSelection{repo().chooseServer(action.id)}}
            is MoskomAction.Mode->execute {reconfigure{repo().routes.apply(action.id,_state.value.dns,_state.value.customDns)}}
            is MoskomAction.Dns->execute {
                try{withContext(Dispatchers.Default){MoskomPolicy.dns(action.id,action.custom)}}catch(_:IllegalArgumentException){_state.update{it.copy(error=R.string.moskom_dns_invalid)};return@execute}
                reconfigure{repo().routes.apply(_state.value.mode,action.id,action.custom)};_state.update{it.copy(message=R.string.moskom_dns_saved)}
            }
            MoskomAction.Rules->execute {
                var changed=false;reconfigure{changed=repo().routes.update{value->_state.update{it.copy(progress=value)}}}
                _state.update{it.copy(progress=null,message=if(changed)R.string.moskom_rules_updated else R.string.moskom_rules_current)}
            }
        }
    }
    private suspend fun changeSelection(change:()->Unit) {
        val running=native.value.isRunning;if(running)stop()
        withContext(Dispatchers.IO){change()};catalog()
        if(running&&_state.value.server.isNotBlank())eventChannel.send(MoskomEvent.Start)
    }
    private suspend fun reconfigure(change:()->Unit) {
        val running=native.value.isRunning;if(running)stop()
        try {withContext(Dispatchers.IO){change()};catalog()}finally {if(running&&_state.value.server.isNotBlank())eventChannel.send(MoskomEvent.Start)}
    }
    private suspend fun stop() {
        val previous=daemon.value.first
        val handled=suspendCancellableCoroutine<Boolean> { continuation ->
            MessageHelper.sendMsg2ServiceForResult(context,AppConfig.MSG_STATE_STOP,""){result->if(continuation.isActive)continuation.resume(result)}
        }
        if(handled)withTimeout(10000){daemon.first { it.first>previous&&!it.second }}
        connectionTimeout?.cancel();_state.update{it.copy(connecting=false)}
    }
    private fun execute(block:suspend()->Unit) {
        if(!state.value.loaded||mutex.isLocked)return
        operation=scope.launch {
            if(!mutex.tryLock())return@launch
            _state.update{it.copy(busy=true,error=0,message=0,progress=null)}
            try{block()}catch(cancelled:CancellationException){throw cancelled}catch(error:Exception){failure(error)}
            finally{mutex.unlock();_state.update{it.copy(busy=false,progress=null)}}
        }
    }
    private suspend fun sync(force:Boolean,prefer:Boolean):Int {
        if(repository==null)return -1
        return try {
            val snapshot=withContext(Dispatchers.IO){repo().accountSnapshot()}
            val revoked=withContext(Dispatchers.IO){repo().selectedWillBeRemoved(snapshot)}
            if(revoked)stop()
            val failures=withContext(Dispatchers.IO){repo().applySnapshot(snapshot,force,prefer)}
            val changed=withContext(Dispatchers.IO){repo().changed}
            catalog();if(changed&&native.value.isRunning)LauncherManager.restartService(context)
            _state.update{it.copy(message=if(failures>0)R.string.moskom_partial_sync else 0)}
            failures
        }catch(cancelled:CancellationException){throw cancelled}
        catch(_:MoskomAuthException){if(_state.value.group.startsWith(MoskomPolicy.CABINET))stop();withContext(Dispatchers.IO){repo().forgetAccount()};catalog();_state.update{it.copy(error=R.string.moskom_login_again)};-1}
        catch(error:Exception){failure(error);-1}
    }
    private suspend fun service() {
        if(repository==null)return
        try{val data=withContext(Dispatchers.IO){repo().service()};_state.update{it.copy(notice=data.first,nodes=data.second,serviceFailed=false)}}
        catch(cancelled:CancellationException){throw cancelled}catch(_:Exception){_state.update{it.copy(serviceFailed=true)}}
    }
    private suspend fun checkUpdate(manual:Boolean) {
        if(repository==null||(!manual&&_state.value.busy))return
        try{
            val release=withContext(Dispatchers.IO){repo().appUpdate()}
            val deferred=release!=null&&!manual&&withContext(Dispatchers.IO){repo().deferred(release)}
            if(release!=null&&!deferred)_state.update{it.copy(release=release)}
            else if(manual)_state.update{it.copy(message=R.string.moskom_app_current)}
        }catch(cancelled:CancellationException){throw cancelled}catch(error:Exception){if(manual)failure(error)}
    }
    private fun failure(error:Exception) {
        // Exceptions from URL/parser libraries can contain credentials; log only a sanitized cause.
        LogUtil.e(AppConfig.TAG,"Moskom account/routing operation failed",IllegalStateException(error.javaClass.simpleName))
        _state.update{it.copy(error=if(error is MoskomAuthException)R.string.moskom_login_again else R.string.moskom_operation_error)}
    }
    fun close(){connectionTimeout?.cancel();repository?.close();ownerJob.cancel();eventChannel.close()}
}
