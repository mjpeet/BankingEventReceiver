using Moq;
using BankingApi.EventReceiver;

namespace BankingApi.EventReceiver.Test.TestHelpers;

public static class MockServiceBusReceiver
{
    public static Mock<IServiceBusReceiver> Create()
    {
        return new Mock<IServiceBusReceiver>();
    }

    public static Mock<IServiceBusReceiver> WithMessages(params EventMessage[] messages)
    {
        var mock = Create();
        var messageQueue = new Queue<EventMessage>(messages);

        mock.Setup(x => x.Peek())
            .Returns(() => Task.FromResult(messageQueue.Count > 0 ? messageQueue.Dequeue() : null));

        return mock;
    }

    public static Mock<IServiceBusReceiver> WithNoMessages()
    {
        var mock = Create();
        mock.Setup(x => x.Peek())
            .Returns(Task.FromResult<EventMessage?>(null));
        return mock;
    }
}