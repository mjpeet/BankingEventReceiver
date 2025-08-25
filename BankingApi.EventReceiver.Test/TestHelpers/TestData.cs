using BankingApi.EventReceiver;

namespace BankingApi.EventReceiver.Test.TestHelpers;

public static class TestData
{
    public static class BankAccounts
    {
        public static BankAccount WithBalance(decimal balance)
        {
            return new BankAccount
            {
                Id = Guid.NewGuid(),
                Balance = balance
            };
        }

        public static BankAccount WithIdAndBalance(Guid id, decimal balance)
        {
            return new BankAccount
            {
                Id = id,
                Balance = balance
            };
        }
    }

    public static class EventMessages
    {
        public static EventMessage CreditMessage(Guid bankAccountId, decimal amount)
        {
            return new EventMessage
            {
                Id = Guid.NewGuid(),
                MessageBody = $$"""
                {
                  "id": "{{Guid.NewGuid()}}",
                  "messageType": "Credit",
                  "bankAccountId": "{{bankAccountId}}",
                  "amount": {{amount}}
                }
                """,
                ProcessingCount = 0
            };
        }

        public static EventMessage DebitMessage(Guid bankAccountId, decimal amount)
        {
            return new EventMessage
            {
                Id = Guid.NewGuid(),
                MessageBody = $$"""
                {
                  "id": "{{Guid.NewGuid()}}",
                  "messageType": "Debit",
                  "bankAccountId": "{{bankAccountId}}",
                  "amount": {{amount}}
                }
                """,
                ProcessingCount = 0
            };
        }

        public static EventMessage InvalidMessage()
        {
            return new EventMessage
            {
                Id = Guid.NewGuid(),
                MessageBody = """
                {
                  "id": "invalid-guid",
                  "messageType": "Invalid",
                  "bankAccountId": "invalid-guid",
                  "amount": "not-a-number"
                }
                """,
                ProcessingCount = 0
            };
        }
    }
}