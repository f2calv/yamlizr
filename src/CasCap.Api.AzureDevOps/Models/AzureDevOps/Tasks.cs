namespace CasCap.Models;

/// <summary>Response envelope returned by the Azure DevOps task catalogue endpoint.</summary>
/// <remarks>
/// Retrieved by <see cref="CasCap.Abstractions.IApiService.GetAllExtensions(string)"/> from
/// <c>_apis/distributedtask/tasks</c>. The catalogue is the only way to resolve the task identifier
/// a classic step carries into the <c>Task@Major</c> form a YAML step needs.
/// </remarks>
public class Tasks
{
    /// <summary>Number of tasks returned in <see cref="Value"/>.</summary>
    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>Every task installed in the organisation, both in-box and extension supplied.</summary>
    [JsonPropertyName("value")]
    public List<TaskObj>? Value { get; set; }
}
