# Skill: SQLite / EF Core data access (`MdcAi.ChatUI.LocalDal`)

Everything about persisting conversations, categories, chat settings, and adding schema changes/migrations in this app.

---

## Project & location

- Project: `Source/Desktop/MdcAi.ChatUI.LocalDal/MdcAi.ChatUI.LocalDal.csproj`
- Pure .NET 9 class library (no WinUI). Namespace `MdcAi.ChatUI.LocalDal`. Referenced by `MdcAi.ChatUI`, `MdcAi.Extensions.WinUI`, and the main `MdcAi` app.
- Packages: `Microsoft.EntityFrameworkCore.Sqlite` 9.0.4, `Microsoft.EntityFrameworkCore.Tools` (for `dotnet ef`), `FlexLabs.EntityFrameworkCore.Upsert` 9.0.0.
- The database is a local SQLite file, **`Chats.db`**, stored in the app's local-app-data folder. A bootstrap copy is shipped as an **embedded resource** in this project and copied out on first run.

## Where the DB physically lives

- `AppServices.GetLocalDataFolder()`:
  - **Unpackaged** → `%LOCALAPPDATA%\MDCAI`
  - **Packaged (MSIX)** → `ApplicationData.Current.LocalFolder.Path`
- The connection is constructed when `App` registers the context: path = `Path.Combine(AppServices.GetLocalDataFolder(), "Chats.db")`.
- `UserProfileDbContext(string dbPath)` constructor: if the file isn't present, it copies the embedded resource `MdcAi.ChatUI.LocalDal.Chats.db` (an `EmbeddedResource` in the csproj) to that path. **So a fresh install gets a pre-shipped (migrated) empty DB.**

## How contexts are obtained and used

Two Dev/DI-registered contexts exist:

| Type | Purpose | Where defined |
|---|---|---|
| `UserProfileDbContext` | plain EF DbContext with a `Log` callback | `MdcAi.ChatUI.LocalDal/UserProfileDbContext.cs` |
| `UserProfileDbContextWithTrans` | wraps a context in a begun transaction | `MdcAi.Extensions.WinUI/UserProfileDbContextWithTrans.cs` |

- **Single-query / read**: `await using var ctx = AppServices.GetUserProfileDb();` (`AppServices.GetUserProfileDb` is a Castle resolve helper).
- **Multi-statement atomic writes**: `Observable.Using(() => AppServices.GetUserProfileDbTrans(), token => { ... token.Ctx ... token.Trans.Commit() })` — disposes both ctx + transaction together. Use this when a save spans several entities (e.g. `SaveCmd` in `ConversationVm`, category+settings creation).
- Set of style shapes in the code:

Read-only (`FromSqlRaw`, `FirstOrDefault`):
```csharp
await using var ctx = AppServices.GetUserProfileDb();
return await ctx.Conversations.FirstOrDefaultAsync(c => c.IdConversation == id);
```

Bulk updates via `ExecuteUpdateAsync` (no change-tracking, single statement):
```csharp
await ctx.Conversations.Where(c => c.IdConversation == id)
          .ExecuteUpdateAsync(c => c.SetProperty(p => p.IsTrash, true));
```

Deletes via `ExecuteDeleteAsync`:
```csharp
await ctx.Messages.Where(m => MessageTrashBin.Contains(m.IdMessage)).ExecuteDeleteAsync();
```

Upserts via `FlexLabs.EntityFrameworkCore.Upsert`:
```csharp
await ctx.ChatSettings.Upsert(dbSettings).RunAsync();   // insert-or-update by key
```

> Note: SQLite + EF runs row-by-row in some write paths (e.g. the conversation `SaveCmd` upserts each message). Keep the transaction wrapper for multi-row saves.

## The domain model / schema

Tables: `ChatSettings`, `Categories`, `Conversations`, `Messages`.

| Entity | Table | PK / notes | Interesting columns |
|---|---|---|---|
| `DbChatSettings` | `ChatSettings` | `[Key]` `IdSettings` (string) | `Model`, `Premise`, `Streaming`(bool=1), `Temperature`, `TopP`, `FrequencyPenalty`, `PresencePenalty` (decimals = 1). Referenced by Categories & Conversations' optional override. |
| `DbCategory` | `Categories` | `[Key]` `IdCategory` | `IdSettings`(FK→settings, req, cascade), `Name`, `Description`, `IconGlyph`, `IsTrash`. |
| `DbConversation` | `Conversations` | `[Key]` `IdConversation` | `IdCategory`(FK, optional), `IdSettingsOverride`(FK→settings, optional), `Name`, `IsTrash`, `CreatedTs`. Has `Messages` nav. |
| `DbMessage` | `Messages` | `[Key]` `IdMessage` | `IdConversation`(FK), `IdMessageParent`(self-ref for forking), `Version`(int), `IsCurrentVersion`(bool), `CreatedTs`, `Role`, `Content`, `Model`(nullable — model id that produced this message, null on user/legacy), `IsTrash`. |

`IsTrash` (bool) is a soft-delete flag on all three aggregate tables (`Categories`, `Conversations`, and message `IsTrash`).

### FKs & cascade
Configured in `OnModelCreating` (`UserProfileDbContext.cs`):

- `Conversation.HasMany(Messages).WithOne(Conversation).HasForeignKey(IdConversation)`
- `Conversation.HasOne(SettingsOverride).WithMany().HasForeignKey(IdSettingsOverride).IsRequired(false)`
- `Conversation.HasOne(Category).WithMany().HasForeignKey(IdCategory).IsRequired(false)`
- `Category.HasOne(Settings).WithMany().HasForeignKey(IdSettings).IsRequired()` → cascade delete.

## How the conversation fork-tree is stored / rebuilt (important)

The in-memory chat is a doubly-linked tree with versions (see `Skills/Reactive`). Persistence maps it to the flat `Messages` table via:

- `ConversationVmExt.ToDbConversation(convo)` → creates a `DbMessage` row **per version** of every node (`ChatMessageVmExt.ToDbMessages`): `IdMessageParent` = parent's id, `Version` = 1-based version index, `IsCurrentVersion` = whether it's the currently selected one.
- Rehydrating uses `ChatMessageVmExt.FromDbMessages(rows, convo)` → flattens rows back into the doubly-linked `Head`/`Tail` tree, preserving version lists on each selector.

> ⚠️ **Edge case / warning**: Because each version is a separate row sharing the same parent id, loading must group by parent + order by version to reconstruct correctly. Do not change the `IdMessageParent`/`Version`/`IsCurrentVersion` scheme without also updating both `ToDbMessages` and `FromDbMessages` in lockstep.

## Migrations

Migrations live under `Source/Desktop/MdcAi.ChatUI.LocalDal/Migrations/`. Add them with the helper script `MigrateEFCore.ps1`:

```powershell
# in the LocalDal project dir
.\MigrateEFCore.ps1 -MigrationName MySchemaChange
```

That runs `dotnet ef migrations add <name>` against the project then `dotnet ef database update --connection "Data Source=Chats.db"`.

### Migration history (chronological)
1. `20231206123521_InitialCreate` — tables + seed "default" category; `SystemMessage` lived on category.
2. `20231214161555_Settings` — adds `IdSettingsOverride` (Conversations), `IdSettings` (Categories), introduces the `ChatSettings` table; moves the system prompt into `ChatSettings.Premise`.
3. `20231221191208_Category Icon` — new nullable `IconGlyph` column.
4. `20231221191321_Category Sys Message Drop` — drops `Categories.SystemMessage`.
5. `20231222172940_Category IsTrash` — adds `Categories.IsTrash`.
6. `20260827191757_UpgradeToEFCore9` — **data-only**, no schema change: updates the seed `general` ChatSettings row to `Model="gpt-4o"` + newer premise. (Far-future timestamp because EF 9 regenerated the snapshot and the seed data drifted; it's a data-sync migration, not a calendar change.)
7. `20260828195212_MessagesModel` — adds nullable `Messages.Model` (model id that produced the message; null on user/legacy rows). `ChatMessageVm` stamps it when a completion starts, `ToDbMessages`/`FromDbMessages` round-trip it, and `ConversationVm` defaults the picker to the last AI reply's model when settings reload.

Snapshot: `UserProfileDbContextModelSnapshot.cs` is the canonical current schema+seed.

## Applying migrations at startup

`App.xaml.cs` runs, on startup via an `Observable.FromAsync`:
```csharp
await using var db = AppServices.GetUserProfileDb();
await db.Database.MigrateAsync();
```
This ensures the user's on-disk `Chats.db` is upgraded to the latest schema on launch. Remember to keep the **embedded default `Chats.db`** in sync too (see below).

## when you change the schema

1. Write the migration as described above (only if the change isn't already covered by an existing migration).
2. Update the **entity classes** + `OnModelCreating` + the snapshot.
3. Update the **shipped `Chats.db`** embedded resource so that fresh installs get a compatible schema:
   - The csproj `EmbeddedResource Include="Chats.db"` points to the file in the project folder.
   - Regenerate/refresh that file (e.g. copy a freshly-`database update` output into `/Source/Desktop/MdcAi.ChatUI.LocalDb/Chats.db`).
4. Re-verify `CreateDefaultChatSettings` seeds match the migrations' final seed (EF complains if the seed data diverges from what the migration emitted — that's how migration #6 appeared).

## Style / conventions in this project

- `Nullable` disabled; class names `DbXxx` and the context `UserProfileDbContext`.
- Uses `@`-verbatim SQL only where LINQ can't (see `ConversationsVm.cs` `FromSqlRaw` category ordering query). Prefer LINQ + `ExecuteUpdateAsync`/`ExecuteDeleteAsync`/`Upsert` elsewhere.
- File-scoped namespaces with `using` after the `namespace ...;` line.
- Every file: Apache-2.0 copyright header.

## Test data / safe DB to inspect

The repo also checks in a `Chats.db` in the LocalDal project **and** a `Chats.db` under a `bin` folder for quick probing — but the authoritative copies are the embedded resource + the shipped file in the project dir. If you need a clean DB for debugging, copy out the embedded resource, or just delete the user's `Chats.db` and let the app extract a fresh one.

---

Read next: `Skills/Reactive` (the in-memory tree it maps to), `Skills/OpenAiApi` (what's stored in message Content).

