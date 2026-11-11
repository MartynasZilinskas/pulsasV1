# Itsaplan project

- Team: AtvirasTinklas (team ID: 5).
- Project: PULSAS (project ID: 77).
- Use qualified project ref `5.PULSAS` for project-scoped calls.
- Issue identifiers use `PULSAS-N`.
- This mapping does not grant write authorization.

## C# conventions

- Default to one type per file, with the filename matching the type name. Tightly
  owned supporting types may share the file; nested operation contracts are an
  intentional exception.
- Give business-operation classes interfaces and prefer `Manager` over `Service`
  naming. Declare operation input/output records inside the corresponding
  interface (for example, `IUserManager.CreateUserInput` and
  `IUserManager.CreateUserResult`). Do not require artificial interfaces for
  utilities or framework-hosted services.
- For async or otherwise cancellable operations, use
  `CancellationToken cancellationToken` as the final, required parameter and pass
  it through downstream calls, including persistence and I/O. Propagate
  cancellation rather than converting it into a business error. Do not add tokens
  to trivial pure synchronous helpers or framework signatures that do not support
  cancellation.
- Do not mark classes or records as `sealed` unless explicitly requested.
- Prefer immutable types and records for contracts and domain/application data
  where appropriate, using get-only or `init` properties. Do not force service
  classes into records.
- Records provide only shallow immutability. Prefer immutable collection
  representations where practical; arrays and read-only views can still expose
  mutable data. No specific package is required.
- Persistence models, including EF Core entities, may remain mutable. Apply this
  convention to new or changed code without unrelated broad refactoring.
