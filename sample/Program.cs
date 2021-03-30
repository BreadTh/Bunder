using System;
using System.Linq;
using System.Threading.Tasks;
using BreadTh.Bunder;
using BreadTh.Bunder.samples;
using RabbitMQ.Client;

var rng = new Random();

async Task<ConsumptionOutcome> RacerAttemptConsumer(Envelope<Participant> envelope) 
{
    const string timeSpan = "hh':'mm':'ss'.'ff";
    const string timeStamp = "hh':'mm':'ss'.'ff";
    var now = DateTime.UtcNow;
    var consoleLogPrefix = 
        $"[{envelope.history.enqueueTime.original.ToString(timeStamp)} => {now.ToString(timeStamp)} -- {envelope.traceId}]";

    var randomNumber = rng.Next(100);
    int threshold = envelope.letter.accuracyPercentage;

    if (randomNumber <= threshold)
        return await Task.FromResult(new ConsumptionSuccess(
            $"{envelope.letter.name} was **LUCKY** ({randomNumber} <= {threshold})" +
            $" and finished after {envelope.history.retryCounter + 1} attempts! " +
            $"Their total time was {(now - envelope.history.enqueueTime.original).ToString(timeSpan)}"));
    
    if (envelope.history.retryCounter < 9)
    { 
        var timeout = TimeSpan.FromMilliseconds((envelope.history.retryCounter+1) * 750);
        var log = 
            $"{envelope.letter.name} was unlucky ({randomNumber} > {threshold})" +
            $" on their #{(envelope.history.retryCounter+1)} attempt" +
            $", after waiting for {(now - envelope.history.enqueueTime.latest).ToString(timeSpan)}" +
            $" since their last attempt and must now wait {timeout.ToString(timeSpan)} before they can try again.";
        
        return await Task.FromResult(new ConsumptionRetry(timeout, log));
    }

    return await Task.FromResult(new ConsumptionReject(
        $"{envelope.letter.name} was unlucky ({randomNumber} > {threshold}) " +
             "on their #10 attempt and was disqualified."));
}


BunderQueue<Participant> bunderQueue = 
    new(name: "bunder-demo.racer", connection: new BunderMultiplexer(new ConnectionFactory { HostName = "localhost" }));


void EnqueueName(string name)
{
    if (string.IsNullOrWhiteSpace(name))
        return;

    Participant participant = new()
    {
        name = name,
        accuracyPercentage = rng.Next(10, 31)
    };

    bunderQueue.Enqueue(participant, Guid.NewGuid().ToString())
        .Switch(
            (PublishSuccess _) => { }, //Will be logged with the log listener below, so don't do anything.
            (PublishFailure f) => throw new Exception($"Could not enqueue message because: {f.Reason}")
        );
}


bunderQueue.Undeclare(); //You don't need to do this. In fact, you probably shouldn't be doing this in prod.
//It's useful when you're noodling around with the program, though.

bunderQueue.Declare();//Only needs to be done once, but is idempotent.
//Will throw if the existing declarations are not the same as- and clash with these declarations.

Console.Write("Enter your participants' names separated by semicolon (or just press enter to get 100 numbered participants) ");
var names = Console.ReadLine() ?? "";

if (string.IsNullOrWhiteSpace(names))
    Enumerable.Range(1, 100).Select(x => $"racer {x}").AsParallel().ForAll(EnqueueName);

else
    foreach (var racerName in names.Split(';'))
        EnqueueName(racerName);


await Task.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine("The games will now start.");
await Task.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine("3..");
await Task.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine("2..");
await Task.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine("1..");
await Task.Delay(TimeSpan.FromSeconds(1));
Console.WriteLine("GO!");
bunderQueue.AddListener(RacerAttemptConsumer);
await Task.Delay(-1);