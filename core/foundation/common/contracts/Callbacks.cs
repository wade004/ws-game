namespace Core.Foundation.Common
{
    /// <summary>
    /// 通用无参、无返回值回调委托。供只需要"通知发生了一件事"、不携带任何参数的回调点复用
    /// （例如 02_引擎适配层.md 1.1 节 IWindow.onCloseRequested 的 callback: Callback）。
    /// 携带参数的回调一律按各自接口的语义定义具名委托（见 engine_adapter/contracts 下各接口文件），
    /// 不复用本委托、也不使用裸 Action/Func。
    /// </summary>
    public delegate void Callback();
}
