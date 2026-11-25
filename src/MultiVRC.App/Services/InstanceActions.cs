using MultiVRC.Core;

namespace MultiVRC.App.Services;

public sealed class InstanceActions
{
    public enum Dialog { None, Edit, Options, Logs }

    public string? InstanceId { get; private set; }

    public (double X, double Y)? MenuAt { get; private set; }

    public Dialog Open { get; private set; }

    public string? Dragging { get; set; }

    public event Action? Changed;

    public void ShowMenu(string instanceId, double x, double y)
    {
        InstanceId = instanceId;
        MenuAt = (x, y);
        Changed?.Invoke();
    }

    public void CloseMenu()
    {
        if (MenuAt is null) return;
        MenuAt = null;
        Changed?.Invoke();
    }

    public void ShowDialog(string instanceId, Dialog dialog)
    {
        InstanceId = instanceId;
        MenuAt = null;
        Open = dialog;
        Changed?.Invoke();
    }

    public void CloseDialog()
    {
        Open = Dialog.None;
        Changed?.Invoke();
    }

    public void DropOn(Library library, string targetId)
    {
        if (Dragging is not { } dragged || dragged == targetId) { Dragging = null; return; }
        var order = library.Instances().Select(i => i.Id).ToList();
        var to = order.IndexOf(targetId);
        order.Remove(dragged);
        order.Insert(Math.Clamp(to, 0, order.Count), dragged);
        library.Reorder(order);
        Dragging = null;
        Changed?.Invoke();
    }
}
