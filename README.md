# EF Core anti-patterns

Companion code for Darrel Schreyer's LinkedIn article, *Your DbContext Is Already the Repository: Three EF Core Anti-Patterns Worth Unlearning* ([article link]).

`SharedTrackerDemo` is a .NET 8 console app. It uses EF Core 8 against an in-memory SQLite database to show what a shared change tracker actually writes, and what changes when each unit of work gets its own short-lived `DbContext`.

## The three anti-patterns

These are patterns teams commonly inherit: joining an existing codebase, picking up a contract, or modernising a .NET stack. Each one made sense to someone at the time.

### 1. A generic repository over EF Core

`DbSet<T>` is already the repository, and `DbContext` is already the Unit of Work. A generic `IRepository<T>` (`GetById`, `GetAll`, `Add`, `Update`, `Remove`) rarely adds a capability. It hides the ones you already have: `Include`, projections, `AsNoTracking`, `AsSplitQuery`, `ExecuteUpdate` / `ExecuteDelete`, compiled queries, and composing an `IQueryable` before it hits the database.

### 2. Unit of Work as an implementation detail

The unit of work is not a type you inject. It is the lifetime of a `DbContext` instance plus the call to `SaveChanges`. Everything that context tracks between creation and save is the unit. An `IUnitOfWork` whose `Commit()` forwards to `SaveChanges` hides which context you have, how long it has been alive, and what else it is tracking.

### 3. One scoped `DbContext` shared across a request

`AddDbContext` registers the context as scoped, so every service in a request receives the same instance. One handler and one `SaveChanges` is a reasonable shape. Several services writing into the same change tracker is not: any save flushes every pending change, including mutations nobody meant to persist.

## What the demo shows

`GreetingService` loads a customer and "formats" the name on the tracked entity (trim, then upper case) so a confirmation email can greet them. `OrderService` adds an order and calls `SaveChanges`. It never touches `Customer`.

| Registration | Writes EF Core sends |
| --- | --- |
| One scoped `DbContext` shared by both services | `UPDATE Customers`, `INSERT Orders` |
| `AddDbContextFactory` | `INSERT Orders` |
| `AddPooledDbContextFactory` | `INSERT Orders` |

With the shared tracker, the customer's stored name becomes `AROHA NGATA`. With either factory, the row is left as it was seeded: two leading spaces, `Aroha Ngata`, and one trailing space. A `WriteLog` interceptor records every INSERT, UPDATE and DELETE. `Program.cs` checks the results and exits with code 1 if any check fails.

The factory cases also run the original mutating greeting inside its own short-lived context. Disposal throws that change away, so isolation alone stops the leak. The projection version makes the intent obvious: read the name, format the string, and leave the row alone.

## When a repository still makes sense

Skip the generic repository by default. A repository earns its place when:

- It is an aggregate-root repository, with specific, intent-revealing methods rather than generic CRUD.
- It encapsulates a complex or reused query, so the shape lives in one place.
- You genuinely need to swap persistence, rather than "we might move off SQL Server one day".
- It is an anti-corruption boundary around a legacy schema or another team's database.

Leave `SaveChanges` with the caller. The caller owns the context lifetime, so the caller owns the unit of work. `Illustrations.cs` includes a small `ICustomerRepository` in that shape.

## When to prefer `IDbContextFactory<T>`

Register `AddDbContextFactory`, or `AddPooledDbContextFactory` when you want pooling. Create a context with `await using` for each unit of work. Prefer a factory when:

- **Blazor Server.** A `DbContext` is not thread-safe, and a circuit can live far longer than a request.
- **Background services and hosted workers.** There is no request scope, and a context that lives as long as the worker grows without limit.
- **Parallel work.** One context per concurrent operation.
- **Desktop apps (WPF / MAUI).** Windows and view models live a long time; units of work should not.
- **Independent transactions.** An audit write or outbox entry that must commit, or fail, on its own.

A scoped context is a good fit when the request is simple: one handler, one context, one `SaveChanges`. There is no need to refactor that for its own sake.

## Project layout

```
global.json                  SDK pin: 8.0.400 or later, within the 8.0 band
expected-output.txt          output the demo should print
SharedTrackerDemo/
  SharedTrackerDemo.csproj   net8.0, EF Core Sqlite 8.0.11
  Program.cs                 seeds data, runs the three cases, checks the writes
  Model.cs                   Customer, Order, ShopContext
  ScopedServices.cs          one scoped context shared across the request
  FactoryServices.cs         a short-lived context per unit of work
  WriteLog.cs                interceptor that records INSERT, UPDATE and DELETE
  Illustrations.cs           article snippets; compiled, not run
```

`Illustrations.cs` holds the other article snippets: the generic repository, a no-tracking projection, `ExecuteUpdateAsync`, and an aggregate repository. The project compiles them so they stay valid C#. `Program.cs` does not execute them.

## How to run it

You need the .NET 8 SDK, 8.0.400 or later. `global.json` sets `rollForward` to `latestFeature`, so a later 8.0 SDK is fine. Delete `global.json`, or edit it, if you need to build with a newer major SDK.

```bash
cd SharedTrackerDemo
dotnet run -c Release
```

Compare the console output with [`expected-output.txt`](expected-output.txt). Every check should print `[PASS]`, and the process should finish with `All checks passed.`
