namespace CasCap.Models;

public readonly struct TaskGroupVersion
{
    public TaskGroupVersion(Guid _taskGroupId, int _version)
    {
        taskGroupId = _taskGroupId;
        version = _version;
    }

    public Guid taskGroupId { get; }
    public int version { get; }
}
