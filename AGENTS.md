# Itsaplan project

- Team: AtvirasTinklas (team ID: 5).
- Project: PULSAS (project ID: 77).
- Use qualified project ref `5.PULSAS` for project-scoped calls.
- Issue identifiers use `PULSAS-N`.
- This mapping does not grant write authorization.

## C# conventions

- Prefer `using` directives and short type names over fully qualified type names.
  Use qualification or aliases only where needed to resolve an actual naming ambiguity.
- Default to one type per file, with the filename matching the type name. Keep
  tightly owned entity field-group types in the same file as their entity; they
  need not be nested CLR types. Other tightly owned supporting types may share
  the file; nested operation contracts are an intentional exception.
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

### Operation contracts and composition

- Use Funcky `Option` for optional values and `Either` for expected operation
  failures. Commands without a required success payload return
  `Either<OperationSpecificError, Unit>` (inside `Task` when async), rather than
  success/status enums. Expected rejection or conflict is Left; successful
  no-op is Right. Return data or entities when callers genuinely need them.
- Use descriptive operation and contract names, such as `ProcessAdvertAsync`,
  `ProcessAdvertInput`, and `ProcessAdvertError`. Keep operation contracts in
  their owning interface; use `using static` for readable short contract names.
- Compose pure steps with existing Funcky operators; prefer expression bodies
  where clearer. Keep side effects, resource lifetimes, and cancellation explicit;
  avoid wrappers added merely for syntax.
- Managers make one attempt and report known failures. The delivery layer owns
  bounded retries, dismissal, and logging. Persistence classifies exact provider
  errors; unexpected exceptions and cancellation propagate.
- Apply these conventions to new or changed code without broad refactoring.

### Entity field grouping

- Use record types for identity-less entity field groups, including when the
  containing entity is a class; this does not require converting the entity
  itself to a record.
- When adding or refactoring entity fields, group cohesive fields where this
  improves readability, for example `SignatureVerification` containing `Status`,
  `FailureReason`, and `CheckedAt`, or `Signal` containing `Rssi` and `Snr`.
  Avoid arbitrary grouping; consider indexes and relationships, and keep foreign
  key properties beside their navigation properties.
- For grouping-only EF Core refactors, map groups with `ComplexProperty` to the
  existing table and columns. Preserve column types, nullability, defaults,
  indexes, and constraints; do not introduce a separate table or JSON wrapper.
- Apply these conventions to relevant new or changed code without unrelated
  broad refactoring.
