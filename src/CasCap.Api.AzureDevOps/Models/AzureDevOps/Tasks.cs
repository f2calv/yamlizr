namespace CasCap.Models;

/// <summary>Response envelope returned by the Azure DevOps task catalogue endpoint.</summary>
/// <remarks>
/// Retrieved by <see cref="CasCap.Services.IApiService.GetAllExtensions(string)"/> from
/// <c>_apis/distributedtask/tasks</c>. The catalogue is the only way to resolve the task identifier
/// a classic step carries into the <c>Task@Major</c> form a YAML step needs.
/// </remarks>
public class Tasks
{
    /// <summary>Number of tasks returned in <see cref="value"/>.</summary>
    public int count { get; set; }

    /// <summary>Every task installed in the organisation, both in-box and extension supplied.</summary>
    public List<TaskObj> value { get; set; }
}
