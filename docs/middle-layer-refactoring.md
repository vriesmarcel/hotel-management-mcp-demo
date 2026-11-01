# Middle-layer refactoring

This implements the first application-extraction milestone of
[accepted ADR 0001](adr/0001-shared-hotel-application-layer-and-mcp.md).
It does not implement MCP, OAuth, future date-range availability,
booking confirmation, persistent idempotency, or a new owner UI.

## Boundaries

- `Hotel.Contracts`: typed models, requests, results, errors, and focused
  room, package, reservation, customer, billing, and desktop-authentication
  service interfaces. No controls, SQL types, `DataSet`, or `DataTable`.
- `Hotel.Application`: one small coordinator implementing those interfaces,
  pricing, validation, lifecycle operations, and typed persistence ports.
  It references contracts, not SQL or Windows Forms.
- `Hotel.Sql`: ADO.NET mapping, explicit parameters/column lists,
  operation-scoped connections, and database transactions.
- `Project_HMS`: existing forms, typed list binding, presentation, and
  navigation. Only `Program.cs` composes the concrete application/SQL layers;
  forms receive service contracts through `DesktopServices`.

`CreateReservation` and `CompleteCheckout` each use one serializable
transaction for all participating writes. A transaction-owned SQL application
lock serializes legacy multi-table write operations and avoids competing
write lock orders. This deliberately favors correctness and a small
implementation over write throughput in this desktop demonstration.
Exceptions roll back the whole operation; no repository commits independently.
Reads also use a short serializable transaction so a quote sees coherent rates.

SQL errors become explicit conflict or infrastructure exceptions. SQL tracing
records error numbers only, not statements, values, connection strings, or
SQL exception messages. Forms translate failures to error dialogs.

## Verified database, 2026-11-01

Read-only inspection of SQL Server at `localhost,1433` confirmed the
`Project_HMS` database at compatibility level `110`. No live rows were
copied into tests or changed.

| Table | Verified columns |
| --- | --- |
| `Room` | `RId int` primary key; `Category`, `IsBooked nvarchar(50)`; `RoomCost float(53)`; nullable `BId int` |
| `Booking` | `BId int` primary key; `RId`, `FId`, `SId int`; `CheckIn`, `CheckOut date`; `CName`, `CAdd`, `CPhone`, `CNID nvarchar(50)`; `Total`, `Advance`, `Remaining float(53)` |
| `FoodPackage` | `FId int` primary key; `FName nvarchar(50)`; `FPackageCost float(53)` |
| `ServicePackage` | `SId int` primary key; `SName nvarchar(50)`; `SPackageCost float(53)` |
| `Login` | `UId int` primary key; `Username`, `Password`, `Role nvarchar(50)` |

All columns except `Room.BId` are required. There are no identity columns,
default constraints, check constraints, or table triggers. Foreign keys link
booking to its room and both packages, and room's active `BId` to booking.
The verified primary keys are single-column keys.

The live database contained 14 rooms, 12 bookings, 5 food packages, 6 service
packages, and 2 login records. Occupancy uses `Yes`/`No`; roles are `Manager`
and `Owner`. Both package tables contain an existing ID `0` row with an
empty name and zero cost. Callers must explicitly select a real package ID,
including `0` when desired; no packages or guest fields are invented/defaulted.

One existing booking had non-positive stay duration and three unbooked rooms
retained booking links. These historical records were not repaired.
New bookings reject non-positive durations; checkout requires the room's
current active booking link. A stale link on an unbooked room is replaced
only when a new reservation explicitly books that room.

Contracts and pricing use `decimal`; the SQL adapter explicitly maps the
verified legacy `float(53)` columns. This is not a currency/schema migration:
legacy floating-point precision limits remain. No currency or rounding scale
has been assumed.

## Local configuration and migration

Build with Visual Studio's Windows desktop/.NET Framework 4.8 development
workload. No additional NuGet packages are required.

Set `HMS_CONNECTION_STRING` securely in the process or user environment before
launching the desktop application. Alternatively configure a `HotelDatabase`
connection string in the built executable's local
`Project_HMS.exe.config` (build output is ignored). Do not put real credentials
in the tracked `App.config`, shell command arguments, chat, or logs.
The local endpoint verified for this work is `localhost,1433`, database
`Project_HMS`; configure authentication locally.

Before creating rooms, packages, or reservations in the intended database,
explicitly apply `database\001-id-sequences.sql` with the baseline application
and other ID-allocating writers stopped. Review the intended database/backup
first. This migration **has not been applied to the shared live database**.
It only adds four SQL sequences, initialized above each table's existing
maximum ID under table locks. It does not change rows, constraints, column
types, or existing IDs. Reapplying does not restart existing sequences.
Runtime allocation uses `NEXT VALUE FOR`, never max-plus-one or an editable
form ID. Empty tables work; rollbacks/deletions may leave normal sequence gaps.
Do not run the baseline allocator alongside the refactored allocator.
Missing sequences fail explicitly with migration guidance.

## Tightly coupled correctness changes

The grids, CRUD entry points, search, guest editing, reservation, payment,
print rendering, dashboard/back navigation, and logout workflows remain.
The extraction intentionally corrects these related behaviors:

- New IDs display `New` until the database allocates them at save. Existing
  records are edited by selecting a grid row; Clear resets that selection.
- Stay duration is derived from the two hotel-local dates, not an independently
  editable day count. Room and food are charged per day; extra service once.
  Quote fields are read-only and stale values are cleared on input changes.
  Reservation always recalculates from stored rates inside its transaction.
- All four guest fields are required and constrained to the verified maximum
  lengths. National IDs remain strings; updates match the original selected ID
  even when the user edits it, and report all matching reservation updates.
- Advances must be non-negative and not exceed the calculated total.
  Checkout payment must cover the persisted remaining balance.
- Reservation cannot book an already occupied room. Checkout verifies the
  selected booking is the room's active reservation, settles from the stored
  total, and clears both occupancy and active `BId` atomically. Repeated or
  historical checkout cannot release a later guest's room.
- Room maintenance cannot independently toggle occupancy; use reservation
  or checkout. A new room starts unbooked. Deleting referenced items surfaces
  a conflict instead of leaving an inconsistent partial operation.
- Login can retry after a failure because there is no form-owned closed
  connection. Authentication returns the stored role without passwords.
  The original owner branch had no UI; it now stays on login with an explicit
  unsupported-workflow message, rather than hiding the application or silently
  granting manager routing.
- Bill data/IDs are read-only; the selected typed booking drives checkout.
  Bill print rendering remains in the UI and change is calculated by billing.

Current occupancy still uses the legacy flag, not future date-range overlap.
Serialization protects this existing workflow; it is **not** the future
availability/authorization/idempotency milestone. Desktop password comparison
retains the verified legacy schema's plaintext authentication mechanism.
Do not expose that mechanism as an MCP tool.

## Verification

`Hotel.Tests` is a dependency-free Framework 4.8 executable. Its default run
checks typed assembly boundaries and application rules without SQL.

```powershell
& '<your MSBuild.exe path>' Project_HMS.sln /nologo /verbosity:minimal /p:Configuration=Debug
& .\Hotel.Tests\bin\Debug\Hotel.Tests.exe
```

For the complete local validation, securely configure `HMS_CONNECTION_STRING`
and run:

```powershell
& .\scripts\Test-Refactoring.ps1
```

Alternatively explicitly select your running local SQL container:

```powershell
& .\scripts\Test-Refactoring.ps1 -ContainerName '<your database container>' -Server 'localhost,1433'
```

The latter reads the selected container's credential into process memory only;
it never prints its environment or password. Docker CLI was used because the
requested local computer use skill was unavailable.

The runner creates a uniquely named `Project_HMS_RefactorTest_*` database,
copies live table definitions with `SELECT TOP (0)` (no customer/login data),
recreates verified keys, sets compatibility level `110`, and inserts synthetic
test data. Unsupported schema features stop provisioning instead of being
silently omitted. It applies the sequence migration twice, runs integration
and WinForms event-handler tests, checks the live source's row-count/checksum
fingerprint, and removes only its explicitly marked, owned test database.
Creating/removing that isolated database requires appropriate SQL permissions.
WinForms tests briefly open test windows and dismiss only dialogs belonging
to their own process. Integration execution is bounded to two minutes.

Verified coverage includes fractional pricing, required guest/package
validation, empty tables, literal category search, apostrophe values, login
retry, CRUD, string national-ID updates, booking and checkout atomic rollback
with injected SQL failure, concurrent IDs and occupied-room conflict, payment
validation, historical checkout protection, typed grid binding, desktop
navigation/logout, and bill print-page rendering. This is automated desktop
workflow verification, not a claim of manual visual inspection or physical
printer testing.

On 2026-11-01 the Release solution build and complete runner passed **191
assertions** (including 111 SQL-free contract/application checks), with no
build warnings. The shared database fingerprint remained unchanged and the
owned test database was removed.

No `after-refactoring`/`after-mcp` tag or PR is created here. Publication and
demonstration-tag decisions remain with the owner/coordinating session.
