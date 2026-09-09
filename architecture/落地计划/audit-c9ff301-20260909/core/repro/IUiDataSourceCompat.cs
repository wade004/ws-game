using Core.Foundation.Common;
using Core.Foundation.EventBus;
using Core.Foundation.Expr;
using EventHandler = Core.Foundation.EventBus.EventHandler;
namespace Presentation.Ui
{
    public interface IUiDataSource
    {
        ExprValue? Query(string path);
        SubscriptionHandle Subscribe(Id eventKey, EventHandler handler);
        void Unsubscribe(SubscriptionHandle handle);
    }
}