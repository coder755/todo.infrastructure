using Amazon.CDK;
using Amazon.CDK.AWS.SNS;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.SSM;
using Constructs;

namespace Infrastructure.PubSub;

public class PubSubStack : Stack
{
    private const string BaseNamespace = "todo";
    private const string ServiceName = "pubsub";
    internal PubSubStack(Construct scope, string stackId, PubSubStackProps props) : base(scope, stackId,
        props)
    {
        const string serviceNamespace = BaseNamespace + "." + ServiceName;
        const string dashedServiceNamespace = BaseNamespace + "-" + ServiceName;

        var toProcessDlq = new Queue(this, serviceNamespace + ".toprocess.queue.dlq", new QueueProps
        {
            QueueName = dashedServiceNamespace + "-toprocess-queue-dlq",
        });
        
        var toProcessQueue = new Queue(this, serviceNamespace + ".toprocess.queue", new QueueProps
        {
            QueueName = dashedServiceNamespace + "-toprocess-queue",
            VisibilityTimeout = Duration.Seconds(10),
            RetentionPeriod = Duration.Days(4),
            ReceiveMessageWaitTime = Duration.Seconds(20),
            DeadLetterQueue = new DeadLetterQueue
            {
                Queue = toProcessDlq,
                MaxReceiveCount = 3,
            },
        });
        
        var unused = new StringParameter(this, serviceNamespace + ".stringParameter.toprocess.queue.url", new StringParameterProps
        {
            ParameterName = serviceNamespace + ".toprocess.queue.url",
            StringValue = toProcessQueue.QueueUrl
        });
                
        var topic = new Topic(this, serviceNamespace + ".sns.topic", new TopicProps
        {
            DisplayName = "CompletedStorageTasks",
        });
        
        var unused1 = new StringParameter(this, serviceNamespace + ".stringParameter.sns.topic.arn", new StringParameterProps
        {
            ParameterName = serviceNamespace + ".sns.topic.arn",
            StringValue = topic.TopicArn
        });
    }
}