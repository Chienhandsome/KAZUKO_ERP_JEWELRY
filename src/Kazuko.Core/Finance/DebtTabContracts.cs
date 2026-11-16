namespace Kazuko.Core.Finance;

public enum DebtTabKind { Customer, Supplier, WorkerGold }

public interface IDebtTabFactory
{
    object CreateView(DebtTabKind tab);
}
