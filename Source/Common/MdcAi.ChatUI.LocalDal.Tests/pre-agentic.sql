CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "Categories" (
    "IdCategory" TEXT NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY,
    "Name" TEXT NULL,
    "SystemMessage" TEXT NULL,
    "Description" TEXT NULL
);

CREATE TABLE "Conversations" (
    "IdConversation" TEXT NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY,
    "IdCategory" TEXT NULL,
    "Name" TEXT NULL,
    "IsTrash" INTEGER NOT NULL,
    "CreatedTs" TEXT NOT NULL,
    CONSTRAINT "FK_Conversations_Categories_IdCategory" FOREIGN KEY ("IdCategory") REFERENCES "Categories" ("IdCategory")
);

CREATE TABLE "Messages" (
    "IdMessage" TEXT NOT NULL CONSTRAINT "PK_Messages" PRIMARY KEY,
    "IdMessageParent" TEXT NULL,
    "IdConversation" TEXT NULL,
    "Version" INTEGER NOT NULL,
    "IsCurrentVersion" INTEGER NOT NULL,
    "CreatedTs" TEXT NOT NULL,
    "Role" TEXT NULL,
    "Content" TEXT NULL,
    "IsTrash" INTEGER NOT NULL,
    CONSTRAINT "FK_Messages_Conversations_IdConversation" FOREIGN KEY ("IdConversation") REFERENCES "Conversations" ("IdConversation")
);

INSERT INTO "Categories" ("IdCategory", "Description", "Name", "SystemMessage")
VALUES ('default', 'General Purpose AI Assistant', 'General', 'You are a helpful but cynical and humorous assistant (but not over the top). You give short answers, straight, to the point answers. Use md syntax and be sure to specify language for code blocks.');
SELECT changes();


CREATE INDEX "IX_Conversations_IdCategory" ON "Conversations" ("IdCategory");

CREATE INDEX "IX_Messages_IdConversation" ON "Messages" ("IdConversation");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20231206123521_InitialCreate', '9.0.4');

ALTER TABLE "Conversations" ADD "IdSettingsOverride" TEXT NULL;

ALTER TABLE "Categories" ADD "IdSettings" TEXT NOT NULL DEFAULT '';

CREATE TABLE "ChatSettings" (
    "IdSettings" TEXT NOT NULL CONSTRAINT "PK_ChatSettings" PRIMARY KEY,
    "Model" TEXT NULL,
    "Streaming" INTEGER NOT NULL,
    "Temperature" TEXT NOT NULL,
    "TopP" TEXT NOT NULL,
    "FrequencyPenalty" TEXT NOT NULL,
    "PresencePenalty" TEXT NOT NULL,
    "Premise" TEXT NULL
);

UPDATE "Categories" SET "IdSettings" = 'general', "SystemMessage" = NULL
WHERE "IdCategory" = 'default';
SELECT changes();


INSERT INTO "ChatSettings" ("IdSettings", "FrequencyPenalty", "Model", "Premise", "PresencePenalty", "Streaming", "Temperature", "TopP")
VALUES ('general', '1.0', 'gpt-4-1106-preview', 'You are a helpful but cynical and humorous assistant (but not over the top). You give short answers, straight, to the point answers. Use md syntax and be sure to specify language for code blocks.', '1.0', 1, '1.0', '1.0');
SELECT changes();


CREATE INDEX "IX_Conversations_IdSettingsOverride" ON "Conversations" ("IdSettingsOverride");

CREATE INDEX "IX_Categories_IdSettings" ON "Categories" ("IdSettings");

CREATE TABLE "ef_temp_Categories" (
    "IdCategory" TEXT NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY,
    "Description" TEXT NULL,
    "IdSettings" TEXT NOT NULL,
    "Name" TEXT NULL,
    "SystemMessage" TEXT NULL,
    CONSTRAINT "FK_Categories_ChatSettings_IdSettings" FOREIGN KEY ("IdSettings") REFERENCES "ChatSettings" ("IdSettings") ON DELETE CASCADE
);

INSERT INTO "ef_temp_Categories" ("IdCategory", "Description", "IdSettings", "Name", "SystemMessage")
SELECT "IdCategory", "Description", "IdSettings", "Name", "SystemMessage"
FROM "Categories";

CREATE TABLE "ef_temp_Conversations" (
    "IdConversation" TEXT NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY,
    "CreatedTs" TEXT NOT NULL,
    "IdCategory" TEXT NULL,
    "IdSettingsOverride" TEXT NULL,
    "IsTrash" INTEGER NOT NULL,
    "Name" TEXT NULL,
    CONSTRAINT "FK_Conversations_Categories_IdCategory" FOREIGN KEY ("IdCategory") REFERENCES "Categories" ("IdCategory"),
    CONSTRAINT "FK_Conversations_ChatSettings_IdSettingsOverride" FOREIGN KEY ("IdSettingsOverride") REFERENCES "ChatSettings" ("IdSettings")
);

INSERT INTO "ef_temp_Conversations" ("IdConversation", "CreatedTs", "IdCategory", "IdSettingsOverride", "IsTrash", "Name")
SELECT "IdConversation", "CreatedTs", "IdCategory", "IdSettingsOverride", "IsTrash", "Name"
FROM "Conversations";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Categories";

ALTER TABLE "ef_temp_Categories" RENAME TO "Categories";

DROP TABLE "Conversations";

ALTER TABLE "ef_temp_Conversations" RENAME TO "Conversations";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_Categories_IdSettings" ON "Categories" ("IdSettings");

CREATE INDEX "IX_Conversations_IdCategory" ON "Conversations" ("IdCategory");

CREATE INDEX "IX_Conversations_IdSettingsOverride" ON "Conversations" ("IdSettingsOverride");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20231214161555_Settings', '9.0.4');

BEGIN TRANSACTION;
ALTER TABLE "Categories" ADD "IconGlyph" TEXT NULL;

UPDATE "Categories" SET "IconGlyph" = NULL
WHERE "IdCategory" = 'default';
SELECT changes();


INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20231221191208_Category Icon', '9.0.4');

CREATE TABLE "ef_temp_Categories" (
    "IdCategory" TEXT NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY,
    "Description" TEXT NULL,
    "IconGlyph" TEXT NULL,
    "IdSettings" TEXT NOT NULL,
    "Name" TEXT NULL,
    CONSTRAINT "FK_Categories_ChatSettings_IdSettings" FOREIGN KEY ("IdSettings") REFERENCES "ChatSettings" ("IdSettings") ON DELETE CASCADE
);

INSERT INTO "ef_temp_Categories" ("IdCategory", "Description", "IconGlyph", "IdSettings", "Name")
SELECT "IdCategory", "Description", "IconGlyph", "IdSettings", "Name"
FROM "Categories";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Categories";

ALTER TABLE "ef_temp_Categories" RENAME TO "Categories";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_Categories_IdSettings" ON "Categories" ("IdSettings");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20231221191321_Category Sys Message Drop', '9.0.4');

BEGIN TRANSACTION;
ALTER TABLE "Categories" ADD "IsTrash" INTEGER NOT NULL DEFAULT 0;

UPDATE "Categories" SET "IsTrash" = 0
WHERE "IdCategory" = 'default';
SELECT changes();


INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20231222172940_Category IsTrash', '9.0.4');

UPDATE "ChatSettings" SET "Model" = 'gpt-4o', "Premise" = 'You are a helpful but cynical and humorous assistant (but not over the top). You give short and straight to the point answers.'
WHERE "IdSettings" = 'general';
SELECT changes();


INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260827191757_UpgradeToEFCore9', '9.0.4');

ALTER TABLE "Messages" ADD "Model" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260828195212_MessagesModel', '9.0.4');

ALTER TABLE "Messages" ADD "Effort" TEXT NULL;

ALTER TABLE "ChatSettings" ADD "Effort" TEXT NULL;

UPDATE "ChatSettings" SET "Effort" = NULL
WHERE "IdSettings" = 'general';
SELECT changes();


INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260829112225_Effort', '9.0.4');

ALTER TABLE "Messages" ADD "Reasoning" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260829132138_MessagesReasoning', '9.0.4');

COMMIT;

