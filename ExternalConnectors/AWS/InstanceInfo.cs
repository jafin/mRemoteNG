using Amazon.EC2.Model;

namespace ExternalConnectors.AWS;

public class InstanceInfo
{
    public string InstanceId { get; }
    public string Name { get; }
    public string Status { get; }
    public string PublicIp { get; }
    public string PrivateIp { get; }
    public InstanceInfo(Instance instance, string name)
    {
        InstanceId = instance.InstanceId;
        Name = name;

        Status = instance.State.Code switch
        {
            0 => "Pending",
            16 => "Running",
            32 => "Shutdown",
            48 => "Terminated",
            64 => "Stopping",
            80 => "Stopped",
            _ => "Unknown"
        };

        PublicIp = instance.PublicIpAddress ?? "";
        PrivateIp = instance.PrivateIpAddress ?? "";

    }
}