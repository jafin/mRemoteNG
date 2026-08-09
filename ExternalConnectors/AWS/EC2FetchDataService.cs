using Amazon;
using Amazon.EC2;
using Amazon.EC2.Model;
using Microsoft.Win32;

namespace ExternalConnectors.AWS;

public static class EC2FetchDataService
{
    private static DateTime lastFetch;
    private static List<InstanceInfo>? lastData;

    // input must be in format "AWSAPI:instanceid" where instanceid is the ec2 instance id, e.g. i-066f750a76c97583d
    public static async Task<string> GetEC2InstanceDataAsync(string input, string region)
    {
        // get secret id
        if (!input.StartsWith("AWSAPI:", StringComparison.Ordinal))
            throw new ArgumentException("calling this function requires AWSAPI: input", nameof(input));
        var instanceId = input[7..];

        // init connection credentials, display popup if necessary
        AWSConnectionData.Init();
        var alldata = await GetEC2IPDataAsync(region);
        var found = alldata.Where(x => string.Equals(x.InstanceId, instanceId, StringComparison.Ordinal))
            .SingleOrDefault();
        return (found == null) ? "" : found.PublicIp;
    }

    private static async Task<List<InstanceInfo>> GetEC2IPDataAsync(string region)
    {
        // caching
        var timeSpan = DateTime.Now - lastFetch;
        if (timeSpan.TotalMinutes < 1 && lastData != null)
            return lastData;

        //AWSConfigs.AWSRegion = AWSConnectionData.region;
        AWSConfigs.AWSRegion = region;
        var awsAccessKeyId = AWSConnectionData.AwsKeyId;
        var awsSecretAccessKey = AWSConnectionData.AwsKey;

        var client = new AmazonEC2Client(awsAccessKeyId, awsSecretAccessKey, RegionEndpoint.EUCentral1);
        var done = false;

        List<InstanceInfo> instanceList = new();
        var request = new DescribeInstancesRequest();
        while (!done)
        {
            var response = await client.DescribeInstancesAsync(request);

            foreach (var reservation in response.Reservations)
            {
                foreach (var instance in reservation.Instances)
                {
                    var vmname = "";
                    foreach (var tag in instance.Tags)
                    {
                        if (string.Equals(tag.Key, "Name", StringComparison.Ordinal))
                        {
                            vmname = tag.Value;
                        }
                    }

                    InstanceInfo inf = new(instance, vmname);
                    instanceList.Add(inf);
                }
            }

            request.NextToken = response.NextToken;

            if (response.NextToken == null)
            {
                done = true;
            }
        }

        lastData = instanceList.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
        lastFetch = DateTime.Now;
        return lastData;
    }


    public static class AWSConnectionData
    {
        private static readonly RegistryKey Key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\mRemoteAWSInterface");

        internal static string AwsKeyId = "";
        internal static string AwsKey = "";

        public static void Init()
        {
            if (AwsKey != "")
                return;
            // display gui and ask for data
            AWSConnectionForm f = new();
            f.tbAccesKeyID.Text = "" + Key.GetValue("KeyID");
            f.tbAccesKey.Text = "" + Key.GetValue("Key");
            _ = f.ShowDialog();

            if (f.DialogResult != DialogResult.OK)
                return;

            // store values to memory
            AwsKeyId = f.tbAccesKeyID.Text;
            AwsKey = f.tbAccesKey.Text;

            // write values to registry
            Key.SetValue("KeyID", AwsKeyId);
            Key.SetValue("Key", AwsKey);
            Key.Close();
        }
    }
}