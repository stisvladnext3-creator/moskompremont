#import <Foundation/Foundation.h>
#import <AppKit/AppKit.h>
#import <Security/Security.h>
#import <SystemConfiguration/SystemConfiguration.h>
#include <sys/stat.h>
#include <unistd.h>

// macOS 11 API only. Authorization credentials never enter argv or a disk file.
static AuthorizationRef auth = NULL;
static __thread char errorText[1024];
static int fail(NSString *message, long status) { snprintf(errorText,sizeof(errorText),"%s (%ld)",message.UTF8String,status); return 1; }
const char *mkr_error(void) { return errorText; }
static NSString *text(const char *s) { return s ? [NSString stringWithUTF8String:s] : nil; }
int mkr_authorize(int interactive) {
 @autoreleasepool {
  if (!auth && AuthorizationCreate(NULL,kAuthorizationEmptyEnvironment,kAuthorizationFlagDefaults,&auth)!=errAuthorizationSuccess) return fail(@"Не удалось подготовить разрешение macOS",-1);
  AuthorizationItem items[]={{"system.preferences",0,NULL,0},{"system.preferences.network",0,NULL,0}};
  AuthorizationRights rights={2,items};
  AuthorizationFlags flags=kAuthorizationFlagExtendRights|kAuthorizationFlagPreAuthorize;
  if (interactive) flags|=kAuthorizationFlagInteractionAllowed;
  OSStatus status=AuthorizationCopyRights(auth,&rights,kAuthorizationEmptyEnvironment,flags,NULL);
  return status==errAuthorizationSuccess?0:fail(@"Разрешите изменение настроек прокси в окне macOS",status);
 }
}
int mkr_auth_export(unsigned char *buffer) { AuthorizationExternalForm form; if (!auth) return 1; OSStatus s=AuthorizationMakeExternalForm(auth,&form);if(s!=0)return 1;memcpy(buffer,form.bytes,sizeof(form.bytes));return 0; }
int mkr_auth_import(const unsigned char *buffer) { AuthorizationExternalForm form;memcpy(form.bytes,buffer,sizeof(form.bytes));if(auth)AuthorizationFree(auth,kAuthorizationFlagDefaults);return AuthorizationCreateFromExternalForm(&form,&auth)==errAuthorizationSuccess?0:1; }
void mkr_auth_release(void) { if(auth){AuthorizationFree(auth,kAuthorizationFlagDefaults);auth=NULL;} }

static SCPreferencesRef preferences(void) { return auth?SCPreferencesCreateWithAuthorization(NULL,CFSTR("MosKomRemont VPN"),NULL,auth):SCPreferencesCreate(NULL,CFSTR("MosKomRemont VPN"),NULL); }
static NSMutableDictionary *allProxies(SCPreferencesRef prefs) {
 NSMutableDictionary *result=[NSMutableDictionary dictionary];
 CFArrayRef array=SCNetworkServiceCopyAll(prefs);if(!array)return result;
 for (id item in (__bridge NSArray*)array) {
  SCNetworkServiceRef service=(__bridge SCNetworkServiceRef)item;
  if(!SCNetworkServiceGetEnabled(service))continue;
  SCNetworkProtocolRef protocol=SCNetworkServiceCopyProtocol(service,kSCNetworkProtocolTypeProxies);if(!protocol)continue;
  NSString *sid=(__bridge NSString*)SCNetworkServiceGetServiceID(service);CFDictionaryRef config=SCNetworkProtocolGetConfiguration(protocol);
  result[sid]=config?[(__bridge NSDictionary*)config copy]:[NSNull null];CFRelease(protocol);
 }
 CFRelease(array);return result;
}
static NSMutableDictionary *readLease(NSString *path,NSString *session) {
 struct stat st;if(lstat(path.fileSystemRepresentation,&st)!=0||!S_ISREG(st.st_mode)||st.st_uid!=getuid()||(st.st_mode&077)!=0||st.st_size>1024*1024)return nil;
 NSData *data=[NSData dataWithContentsOfFile:path];NSMutableDictionary *value=data?[NSJSONSerialization JSONObjectWithData:data options:NSJSONReadingMutableContainers error:NULL]:nil;
 if(![value isKindOfClass:[NSMutableDictionary class]]||![value[@"session"] isEqual:session]||![value[@"version"] isEqual:@1]||![value[@"original"] isKindOfClass:[NSDictionary class]]||![value[@"owned"] isKindOfClass:[NSDictionary class]])return nil;
 return value;
}
static BOOL writeLease(NSString *path,NSDictionary *value) {
 NSData *data=[NSJSONSerialization dataWithJSONObject:value options:0 error:NULL];if(!data)return NO;
 mode_t old=umask(077);BOOL okay=[data writeToFile:path options:NSDataWritingAtomic error:NULL];umask(old);
 return okay&&chmod(path.fileSystemRepresentation,0600)==0;
}
int mkr_proxy_prepare(const char *file,int port,int pid,const char *start,const char *session) {
 @autoreleasepool {
  if(port<1||port>65535)return fail(@"Некорректный порт",0);
  SCPreferencesRef prefs=preferences();if(!prefs)return fail(@"Не удалось прочитать настройки сети",SCError());
  NSMutableDictionary *original=allProxies(prefs),*owned=[NSMutableDictionary dictionary];CFRelease(prefs);
  if(original.count==0)return fail(@"Не найдены сетевые службы с настройками прокси",0);
  for(NSString *sid in original){
   NSMutableDictionary *proxy=original[sid]==[NSNull null]?[NSMutableDictionary dictionary]:[original[sid] mutableCopy];
   proxy[(__bridge NSString*)kSCPropNetProxiesHTTPEnable]=@1;proxy[(__bridge NSString*)kSCPropNetProxiesHTTPProxy]=@"127.0.0.1";proxy[(__bridge NSString*)kSCPropNetProxiesHTTPPort]=@(port);
   proxy[(__bridge NSString*)kSCPropNetProxiesHTTPSEnable]=@1;proxy[(__bridge NSString*)kSCPropNetProxiesHTTPSProxy]=@"127.0.0.1";proxy[(__bridge NSString*)kSCPropNetProxiesHTTPSPort]=@(port);
   proxy[(__bridge NSString*)kSCPropNetProxiesSOCKSEnable]=@1;proxy[(__bridge NSString*)kSCPropNetProxiesSOCKSProxy]=@"127.0.0.1";proxy[(__bridge NSString*)kSCPropNetProxiesSOCKSPort]=@(port);
   proxy[(__bridge NSString*)kSCPropNetProxiesProxyAutoConfigEnable]=@0;proxy[(__bridge NSString*)kSCPropNetProxiesProxyAutoDiscoveryEnable]=@0;
   NSMutableOrderedSet *bypass=[NSMutableOrderedSet orderedSetWithArray:@[@"localhost",@"127.*",@"::1",@"*.local"]];id previous=proxy[(__bridge NSString*)kSCPropNetProxiesExceptionsList];if([previous isKindOfClass:[NSArray class]])[bypass addObjectsFromArray:previous];
   proxy[(__bridge NSString*)kSCPropNetProxiesExceptionsList]=bypass.array;proxy[(__bridge NSString*)kSCPropNetProxiesExcludeSimpleHostnames]=@1;owned[sid]=proxy;
  }
  NSDictionary *lease=@{@"version":@1,@"pid":@(pid),@"started":text(start),@"session":text(session),@"original":original,@"owned":owned};
  if(!writeLease(text(file),lease))return fail(@"Не удалось сохранить прежние настройки прокси",errno);
  return 0;
 }
}
static int change(const char *file,const char *session,BOOL restore) {
 @autoreleasepool {
  NSString *path=text(file);NSMutableDictionary *lease=readLease(path,text(session));if(!lease)return fail(@"Не удалось прочитать резервную копию прокси",0);
  SCPreferencesRef prefs=preferences();if(!prefs)return fail(@"Настройки сети недоступны",SCError());
  if(!SCPreferencesLock(prefs,FALSE)){CFRelease(prefs);return fail(@"Настройки сети заняты другой программой",SCError());}
  SCPreferencesSynchronize(prefs);BOOL okay=YES;int changed=0;
  NSDictionary *original=lease[@"original"],*owned=lease[@"owned"];
  for(NSString *sid in original){
   SCNetworkServiceRef service=SCNetworkServiceCopy(prefs,(__bridge CFStringRef)sid);if(!service){if(!restore)okay=NO;continue;}
   SCNetworkProtocolRef protocol=SCNetworkServiceCopyProtocol(service,kSCNetworkProtocolTypeProxies);CFRelease(service);if(!protocol){if(!restore)okay=NO;continue;}
   CFDictionaryRef current=SCNetworkProtocolGetConfiguration(protocol);id now=current?(__bridge NSDictionary*)current:[NSNull null];
   // Never overwrite a different VPN or a user change, including PAC/bypass changes.
   if(restore){if(![now isEqual:owned[sid]]){CFRelease(protocol);continue;}}
   else if(![now isEqual:original[sid]]){CFRelease(protocol);okay=NO;break;}
   id replacement=restore?original[sid]:owned[sid];
   if(!SCNetworkProtocolSetConfiguration(protocol,replacement==[NSNull null]?NULL:(__bridge CFDictionaryRef)replacement))okay=NO;else changed++;
   CFRelease(protocol);if(!okay)break;
  }
  if(okay&&changed>0)okay=SCPreferencesCommitChanges(prefs)&&SCPreferencesApplyChanges(prefs);
  SCPreferencesUnlock(prefs);CFRelease(prefs);
  if(!okay)return fail(@"Не удалось применить настройки сети; резервная копия сохранена",SCError());
  if(restore)[[NSFileManager defaultManager] removeItemAtPath:path error:NULL];
  return 0;
 }
}
int mkr_proxy_apply(const char *file,const char *session){return change(file,session,NO);}
int mkr_proxy_restore(const char *file,const char *session){return change(file,session,YES);}
int mkr_proxy_snapshot(const char *file) {
 @autoreleasepool {SCPreferencesRef prefs=preferences();if(!prefs)return 1;NSDictionary *all=allProxies(prefs);CFRelease(prefs);return writeLease(text(file),all)?0:1;}
}

static NSDictionary *key(const char *account){return @{(__bridge id)kSecClass:(__bridge id)kSecClassGenericPassword,(__bridge id)kSecAttrService:@"ru.moskompremont.vpn.cabinet",(__bridge id)kSecAttrAccount:text(account)};}
int mkr_keychain_save(const char *account,const unsigned char *bytes,int length) {
 @autoreleasepool {if(length<0||length>65536)return 1;NSDictionary *query=key(account);NSData *data=[NSData dataWithBytes:bytes length:length];OSStatus s=SecItemUpdate((__bridge CFDictionaryRef)query,(__bridge CFDictionaryRef)@{(__bridge id)kSecValueData:data});
 if(s==errSecItemNotFound){NSMutableDictionary *add=[query mutableCopy];add[(__bridge id)kSecValueData]=data;s=SecItemAdd((__bridge CFDictionaryRef)add,NULL);}return s==errSecSuccess?0:fail(@"Не удалось сохранить вход в Связке ключей macOS",s);}
}
int mkr_keychain_load(const char *account,unsigned char *buffer,int capacity) {
 @autoreleasepool {NSMutableDictionary *query=[key(account) mutableCopy];query[(__bridge id)kSecReturnData]=@YES;CFTypeRef value=NULL;OSStatus s=SecItemCopyMatching((__bridge CFDictionaryRef)query,&value);if(s==errSecItemNotFound)return 0;if(s!=errSecSuccess)return -1;NSData *data=CFBridgingRelease(value);if(data.length>(NSUInteger)capacity)return -1;memcpy(buffer,data.bytes,data.length);return (int)data.length;}
}
int mkr_keychain_delete(const char *account) {@autoreleasepool {OSStatus s=SecItemDelete((__bridge CFDictionaryRef)key(account));return s==errSecSuccess||s==errSecItemNotFound?0:1;}}

#include <sys/socket.h>
#include <sys/un.h>
#include <libproc.h>
unsigned int mkr_uid(void){return getuid();}
int mkr_check_peer(int fd,unsigned int expectedUid,int expectedPid){
 uid_t uid;gid_t gid;if(getpeereid(fd,&uid,&gid)!=0||uid!=expectedUid)return 1;
 if(expectedPid>0){pid_t pid=0;socklen_t length=sizeof(pid);if(getsockopt(fd,SOL_LOCAL,LOCAL_PEERPID,&pid,&length)!=0||pid!=expectedPid)return 1;}
 return 0;
}

int mkr_running_apps(unsigned char *buffer,int capacity){
 @autoreleasepool {
  NSMutableArray *rows=[NSMutableArray array];
  for(NSRunningApplication *app in NSWorkspace.sharedWorkspace.runningApplications){
   NSString *path=app.executableURL.path;if(!path||app.processIdentifier==getpid())continue;
   [rows addObject:@{@"id":path,@"name":app.localizedName?:path.lastPathComponent,@"path":path,@"bundle":app.bundleURL.path?:@""}];
  }
  NSData *data=[NSJSONSerialization dataWithJSONObject:rows options:0 error:NULL];if(!data||data.length>(NSUInteger)capacity)return -1;memcpy(buffer,data.bytes,data.length);return (int)data.length;
 }
}
