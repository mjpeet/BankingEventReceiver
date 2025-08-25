What things did you considered of during the implementation?
- TDD cycles for all functionalities
- DI - clean separation of concerns, easily testable architecture
- Background service pattern - for continuous processing, .net hosted service
- repository pattern dbContext abstraction

- mock ServiceBusReceiver to be repalced with real Azure Service Bus connectivity, to process actual Credit/Debit messages and update account balances in the database


Design Decisions & Trade-offs

Message Processing

Service Bus Retry Handling: Used Abandon() instead of ReSchedule() to take advantage of Azure Service Bus’s built-in retry and dead-letter features.

JSON Handling: Created a dedicated TransactionModel since the event message body contained nested JSON.

Processing Approaches: Implemented different methods (ProcessMessage(), ProcessMessageWithRetry(), ProcessMessageWithConcurrency()) to handle various scenarios.

Error Handling

Retry Logic: Classified exceptions into transient (e.g., TimeoutException, DbUpdateException, InvalidOperationException) for retries, while non-transient ones are dead-lettered immediately.

Backoff Strategy: Followed exponential retry timing (5 → 25 → 125 seconds) but simplified implementation by relying on Service Bus abandonment.

Concurrency

Transaction Simulation: Since the in-memory DB doesn’t support transactions, tracked originalBalance to simulate rollback.

Thread Safety: Each worker uses a scoped DbContext to avoid shared state issues.

Testing Strategy

In-Memory Database: Speeds up tests and removes the need for SQL Server during development.

Mock Service Bus: Enables isolated testing without external dependencies.

Coverage: Unit tests for individual scenarios plus integration tests for workflow validation.

Production Readiness

Logging: Added console logging for visibility during execution.

Graceful Shutdown: Implemented CancellationToken support.

Configuration: Structured so it can swap easily between Service Bus implementations.

Database Options: Works with SQL Server in production and in-memory DB for demos/tests.

Key Trade-offs

Complexity vs. Testability: Extra processing methods make tests more flexible but increase complexity.

In-Memory Limitations: Some transaction/concurrency issues can’t be fully tested without a real database.

Mock vs. Real Services: Mocks simplify testing but must be swapped with production-ready services.

Anything was unclear?
- the json examples did not match the EventMessage class
    - the EventMessage class suggests the actual transaction data is nested within MessageBody as a       
  JSON string.


What's Missing

  The MessageWorker class doesn't have structured logging injected - it uses Console.WriteLine() as a placeholder. In production, you'd want:
  private readonly ILogger<MessageWorker> _logger;

  The logging is primarily in the infrastructure layer (hosting/Service Bus) but could be enhanced in the core business logic for better observability.