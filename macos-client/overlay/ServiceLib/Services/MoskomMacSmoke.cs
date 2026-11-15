namespace ServiceLib.Services;
public static partial class MoskomLauncherRuntime {
 // Tests drive the real UI: drain its asynchronous initial selection before the next action.
 public static async Task WaitForMacSmokeIdleAsync(){
  if(!SmokeTest)throw new InvalidOperationException("Метод доступен только локальной проверке.");
  await Task.Delay(250);using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(10));
  await Operations.WaitAsync(deadline.Token);Operations.Release();
 }
}
