// Facebook host plugin'i — iOS (docs/plugins.md). DEPlugin base'den (generated/cpp/c_runtime/DEPlugin.h); SPM urunu FacebookLogin
// (FacebookIos kancasi). Ad = managed sinifin kisa adi. onMessage: main thread, host @try/@catch.
#import "DEPlugin.h"
#import <FBSDKCoreKit/FBSDKCoreKit.h>
#import <FBSDKLoginKit/FBSDKLoginKit.h>

@interface FacebookPlugin : DEPlugin
@end

@implementation FacebookPlugin
{
    FBSDKLoginManager *_login;
    int _pendingId;
}

- (instancetype)init { return [super initWithName:@"Facebook"]; }

- (void)didFinishLaunching:(NSDictionary *)launchOptions
{
    [[FBSDKApplicationDelegate sharedInstance] application:[UIApplication sharedApplication] didFinishLaunchingWithOptions:launchOptions];
    _login = [FBSDKLoginManager new];
    _pendingId = -1;
}

- (BOOL)openURL:(NSURL *)url options:(NSDictionary<UIApplicationOpenURLOptionsKey, id> *)options
{
    BOOL handled = [[FBSDKApplicationDelegate sharedInstance] application:[UIApplication sharedApplication] openURL:url options:options];
    [self send:[NSString stringWithFormat:@"{\"event\":\"deepLink\",\"url\":\"%@\"}", url.absoluteString]];
    return handled;
}

- (void)onMessage:(NSString *)msg
{
    NSDictionary *j = [NSJSONSerialization JSONObjectWithData:[msg dataUsingEncoding:NSUTF8StringEncoding] options:0 error:nil];
    NSString *method = j[@"method"];
    if ([method isEqualToString:@"login"])
    {
        _pendingId = [j[@"id"] intValue];
        NSArray *perms = j[@"permissions"] ?: @[ @"public_profile" ];
        __weak FacebookPlugin *weakSelf = self;
        [_login logInWithPermissions:perms fromViewController:self.viewController handler:^(FBSDKLoginManagerLoginResult *r, NSError *e) {
            [weakSelf replyOk:(r && !r.isCancelled && !e) token:r.token.tokenString user:r.token.userID error:e ? e.localizedDescription : (r.isCancelled ? @"cancel" : nil)];
        }];
    }
    else if ([method isEqualToString:@"logout"])
        [_login logOut];
}

- (void)replyOk:(BOOL)ok token:(NSString *)token user:(NSString *)user error:(NSString *)error
{
    if (_pendingId < 0) return;
    NSMutableDictionary *j = [@{ @"id" : @(_pendingId), @"ok" : @(ok) } mutableCopy];
    if (token) j[@"token"] = token;
    if (user) j[@"userId"] = user;
    if (error) j[@"error"] = error;
    _pendingId = -1;
    NSData *d = [NSJSONSerialization dataWithJSONObject:j options:0 error:nil];
    [self send:[[NSString alloc] initWithData:d encoding:NSUTF8StringEncoding]];
}
@end

DE_REGISTER_PLUGIN(FacebookPlugin)
