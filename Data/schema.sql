CREATE TABLE IF NOT EXISTS CanonicalCharacters (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalName TEXT NOT NULL UNIQUE,
    DisplayName TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    CanonPriority INTEGER NOT NULL DEFAULT 0,
    UserLocked INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE IF NOT EXISTS Characters (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalCharacterId INTEGER NULL,
    Name TEXT NOT NULL,
    InternalName TEXT NULL,
    DisplayName TEXT NULL,
    Description TEXT NOT NULL,
    Personality TEXT NOT NULL,
    Occupation TEXT NOT NULL,
    HomeLocation TEXT NOT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    IsVanilla INTEGER NOT NULL DEFAULT 0,
    IsCustomNpc INTEGER NOT NULL DEFAULT 0,
    IsExtension INTEGER NOT NULL DEFAULT 0,
    LastSeen TEXT NULL,
    SourceModId TEXT NULL,
    SourceModName TEXT NULL,
    SourceModVersion TEXT NULL,
    SourceModAuthor TEXT NULL,
    CharacterFingerprint TEXT NULL,
    LastModified TEXT NULL,
    RawModData TEXT NULL,
    FOREIGN KEY (CanonicalCharacterId) REFERENCES CanonicalCharacters(Id)
);

CREATE TABLE IF NOT EXISTS CharacterSources (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalCharacterId INTEGER NOT NULL,
    SourceModId TEXT NOT NULL,
    SourceType TEXT NOT NULL,
    Priority INTEGER NOT NULL DEFAULT 0,
    Notes TEXT NULL,
    FOREIGN KEY (CanonicalCharacterId) REFERENCES CanonicalCharacters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS CharacterMergeRules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalCharacterId INTEGER NOT NULL,
    MatchName TEXT NULL,
    MatchSourceModId TEXT NULL,
    MatchUniqueId TEXT NULL,
    MatchInternalName TEXT NULL,
    RuleType TEXT NOT NULL,
    Confidence INTEGER NOT NULL DEFAULT 0,
    CreatedBy TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    FOREIGN KEY (CanonicalCharacterId) REFERENCES CanonicalCharacters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS CharacterAliases (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalCharacterId INTEGER NOT NULL,
    Alias TEXT NOT NULL,
    SourceModId TEXT NULL,
    Confidence INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (CanonicalCharacterId) REFERENCES CanonicalCharacters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS CharacterMergeReviewQueue (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CandidateName TEXT NOT NULL,
    CandidateInternalName TEXT NULL,
    SourceModId TEXT NOT NULL,
    SourceModName TEXT NULL,
    SuggestedCanonicalCharacterId INTEGER NULL,
    SuggestedCanonicalName TEXT NULL,
    Confidence INTEGER NOT NULL DEFAULT 0,
    Evidence TEXT NOT NULL,
    Reason TEXT NOT NULL,
    Status TEXT NOT NULL DEFAULT 'Pending',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (SuggestedCanonicalCharacterId) REFERENCES CanonicalCharacters(Id)
);

CREATE TABLE IF NOT EXISTS Relationships (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterA INTEGER NOT NULL,
    CharacterB INTEGER NOT NULL,
    RelationshipType TEXT NOT NULL,
    Strength INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (CharacterA) REFERENCES Characters(Id) ON DELETE CASCADE,
    FOREIGN KEY (CharacterB) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS Events (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Title TEXT NOT NULL,
    Description TEXT NOT NULL,
    DateOccurred TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS Memories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    MemoryText TEXT NOT NULL,
    Importance INTEGER NOT NULL DEFAULT 1,
    CreatedDate TEXT NOT NULL,
    FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS VoiceRules (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    RuleText TEXT NOT NULL,
    FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS DialogueExamples (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    DialogueText TEXT NOT NULL,
    Emotion TEXT NOT NULL DEFAULT 'neutral',
    Topic TEXT NOT NULL DEFAULT 'general',
    FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS GeneratedDialogueHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    CharacterName TEXT NOT NULL,
    Season TEXT NOT NULL,
    Weather TEXT NOT NULL,
    Location TEXT NOT NULL,
    FriendshipLevel INTEGER NOT NULL DEFAULT 0,
    RelationshipContext TEXT NULL,
    Topic TEXT NOT NULL DEFAULT 'general',
    Prompt TEXT NOT NULL,
    DialogueText TEXT NOT NULL,
    Emotion TEXT NOT NULL DEFAULT 'neutral',
    CharacterConsistencyScore INTEGER NOT NULL DEFAULT 0,
    ContextRelevanceScore INTEGER NOT NULL DEFAULT 0,
    RelationshipRelevanceScore INTEGER NOT NULL DEFAULT 0,
    DiversityScore INTEGER NOT NULL DEFAULT 0,
    RepetitionRiskScore INTEGER NOT NULL DEFAULT 0,
    CreatedDate TEXT NOT NULL,
    FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS DialogueSources (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalCharacterId INTEGER NOT NULL,
    SourceModId TEXT NULL,
    FilePath TEXT NOT NULL,
    AssetName TEXT NULL,
    DialogueKey TEXT NOT NULL,
    RawText TEXT NOT NULL,
    Conditions TEXT NULL,
    Season TEXT NULL,
    Weather TEXT NULL,
    Location TEXT NULL,
    HeartLevel INTEGER NULL,
    RelationshipState TEXT NULL,
    SourcePriority INTEGER NOT NULL DEFAULT 0,
    IsActive INTEGER NOT NULL DEFAULT 1,
    LastSeen TEXT NOT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (CanonicalCharacterId) REFERENCES CanonicalCharacters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS DialogueSourceSummaries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalCharacterId INTEGER NOT NULL UNIQUE,
    SummaryText TEXT NOT NULL,
    ToneSummary TEXT NOT NULL,
    CommonTopics TEXT NOT NULL,
    RelationshipPatterns TEXT NOT NULL,
    ImportantCanonFacts TEXT NOT NULL,
    LastGeneratedAt TEXT NOT NULL,
    FOREIGN KEY (CanonicalCharacterId) REFERENCES CanonicalCharacters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS GeneratedDialogueOverrides (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CanonicalCharacterId INTEGER NOT NULL,
    DialogueKey TEXT NOT NULL,
    OriginalDialogueSourceId INTEGER NULL,
    GeneratedText TEXT NOT NULL,
    PromptUsed TEXT NOT NULL,
    SaveContextSnapshot TEXT NOT NULL,
    IsEnabled INTEGER NOT NULL DEFAULT 0,
    IsApproved INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (CanonicalCharacterId) REFERENCES CanonicalCharacters(Id) ON DELETE CASCADE,
    FOREIGN KEY (OriginalDialogueSourceId) REFERENCES DialogueSources(Id)
);

CREATE TABLE IF NOT EXISTS CharacterHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    Timestamp TEXT NOT NULL,
    PreviousData TEXT NOT NULL,
    NewData TEXT NOT NULL,
    ChangeReason TEXT NOT NULL,
    FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS LoreChangeLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    SourceModId TEXT NULL,
    FieldChanged TEXT NOT NULL,
    OldValue TEXT NULL,
    NewValue TEXT NULL,
    Timestamp TEXT NOT NULL,
    FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS UserLoreOverrides (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    OverrideType TEXT NOT NULL,
    FieldName TEXT NOT NULL,
    OverrideValue TEXT NOT NULL,
    Notes TEXT NULL,
    CreatedDate TEXT NOT NULL,
    LastModified TEXT NOT NULL,
    FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ScannedMods (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    UniqueId TEXT NOT NULL UNIQUE,
    Name TEXT NOT NULL,
    Version TEXT NULL,
    Author TEXT NULL,
    IsActive INTEGER NOT NULL DEFAULT 1,
    LastScanTime TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS LoreConflicts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CharacterId INTEGER NOT NULL,
    SourceModId TEXT NULL,
    FieldName TEXT NOT NULL,
    ModValue TEXT NULL,
    OverrideValue TEXT NULL,
    IsReviewed INTEGER NOT NULL DEFAULT 0,
    CreatedDate TEXT NOT NULL,
    ReviewedDate TEXT NULL,
    FOREIGN KEY (CharacterId) REFERENCES Characters(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NULL,
    LastModified TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS ScanHistory (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    TriggerSource TEXT NOT NULL,
    StartedAt TEXT NOT NULL,
    CompletedAt TEXT NOT NULL,
    Success INTEGER NOT NULL,
    ModsScanned INTEGER NOT NULL DEFAULT 0,
    CharactersFound INTEGER NOT NULL DEFAULT 0,
    CharactersAdded INTEGER NOT NULL DEFAULT 0,
    CharactersUpdated INTEGER NOT NULL DEFAULT 0,
    CharactersReactivated INTEGER NOT NULL DEFAULT 0,
    CharactersMarkedInactive INTEGER NOT NULL DEFAULT 0,
    ConflictsFound INTEGER NOT NULL DEFAULT 0,
    ErrorMessage TEXT NULL
);

CREATE TABLE IF NOT EXISTS CharacterValidationResults (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    SourceModId TEXT NOT NULL DEFAULT '',
    SourceModName TEXT NULL,
    Score INTEGER NOT NULL DEFAULT 0,
    Classification TEXT NOT NULL,
    Imported INTEGER NOT NULL DEFAULT 0,
    Evidence INTEGER NOT NULL DEFAULT 0,
    RulesJson TEXT NOT NULL DEFAULT '[]',
    RawModData TEXT NULL,
    LastSeen TEXT NULL,
    UpdatedDate TEXT NOT NULL
);

-- Explainability: a complete trace of the inputs used to generate each dialogue line.
-- Intentionally has no cascading foreign key so explanation history is never auto-deleted.
CREATE TABLE IF NOT EXISTS DialogueGenerationTrace (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    GeneratedDialogueId INTEGER NOT NULL,
    GeneratedAt TEXT NOT NULL,
    CharacterId INTEGER NOT NULL,
    SaveContextSnapshot TEXT NOT NULL DEFAULT '{}',
    MemoriesUsed TEXT NOT NULL DEFAULT '[]',
    RelationshipsUsed TEXT NOT NULL DEFAULT '[]',
    UserOverridesUsed TEXT NOT NULL DEFAULT '[]',
    DialogueSourcesUsed TEXT NOT NULL DEFAULT '[]',
    SourceModsUsed TEXT NOT NULL DEFAULT '[]',
    PromptVersion TEXT NOT NULL DEFAULT '',
    PromptText TEXT NOT NULL DEFAULT '',
    ModelUsed TEXT NOT NULL DEFAULT '',
    PlayerProfileUsed TEXT NOT NULL DEFAULT 'null',
    PlayerRelationshipNotesUsed TEXT NOT NULL DEFAULT '[]',
    PlayerMemoriesUsed TEXT NOT NULL DEFAULT '[]',
    SaveFileLinkUsed TEXT NULL
);

CREATE INDEX IF NOT EXISTS IX_DialogueGenerationTrace_GeneratedDialogueId ON DialogueGenerationTrace(GeneratedDialogueId);

-- Saved scenarios for Game Simulation Mode (test dialogue without launching Stardew Valley).
CREATE TABLE IF NOT EXISTS TestScenarios (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    PlayerName TEXT NOT NULL DEFAULT 'Farmer',
    FarmName TEXT NOT NULL DEFAULT 'Green Acres',
    Year INTEGER NOT NULL DEFAULT 1,
    Season TEXT NOT NULL DEFAULT 'spring',
    Weather TEXT NOT NULL DEFAULT 'clear',
    Location TEXT NOT NULL DEFAULT 'Town',
    FriendshipHearts INTEGER NOT NULL DEFAULT 0,
    RelationshipState TEXT NOT NULL DEFAULT 'Stranger',
    SeenEvents TEXT NOT NULL DEFAULT '',
    CompletedQuests TEXT NOT NULL DEFAULT '',
    CommunityCenterState TEXT NOT NULL DEFAULT 'Not started',
    PlayerProfileId INTEGER NULL,
    IsBuiltIn INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

-- Player/farmer lore profiles (multiple saves / roleplay characters).
CREATE TABLE IF NOT EXISTS PlayerProfiles (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ProfileName TEXT NOT NULL,
    FarmerName TEXT NOT NULL DEFAULT '',
    FarmName TEXT NOT NULL DEFAULT '',
    SaveFileName TEXT NULL,
    SaveFilePath TEXT NULL,
    Description TEXT NOT NULL DEFAULT '',
    Backstory TEXT NOT NULL DEFAULT '',
    Personality TEXT NOT NULL DEFAULT '',
    RoleplayStyle TEXT NOT NULL DEFAULT '',
    PreferredTone TEXT NOT NULL DEFAULT '',
    ImportantHistory TEXT NOT NULL DEFAULT '',
    CurrentGoals TEXT NOT NULL DEFAULT '',
    RelationshipNotes TEXT NOT NULL DEFAULT '',
    CustomLore TEXT NOT NULL DEFAULT '',
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS PlayerProfileRelationships (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerProfileId INTEGER NOT NULL,
    CanonicalCharacterId INTEGER NOT NULL,
    RelationshipType TEXT NOT NULL DEFAULT '',
    RelationshipDescription TEXT NOT NULL DEFAULT '',
    RelationshipStrength INTEGER NOT NULL DEFAULT 0,
    CustomNotes TEXT NOT NULL DEFAULT '',
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (PlayerProfileId) REFERENCES PlayerProfiles(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS PlayerProfileMemories (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerProfileId INTEGER NOT NULL,
    CanonicalCharacterId INTEGER NULL,
    MemoryText TEXT NOT NULL DEFAULT '',
    Importance INTEGER NOT NULL DEFAULT 3,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (PlayerProfileId) REFERENCES PlayerProfiles(Id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS PlayerProfileSaveLinks (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    PlayerProfileId INTEGER NOT NULL,
    SaveFileName TEXT NOT NULL,
    SaveFilePath TEXT NULL,
    LastSeen TEXT NULL,
    IsDefaultForSave INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    FOREIGN KEY (PlayerProfileId) REFERENCES PlayerProfiles(Id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS IX_PlayerProfiles_IsActive ON PlayerProfiles(IsActive);
CREATE INDEX IF NOT EXISTS IX_PlayerProfileRelationships_Profile ON PlayerProfileRelationships(PlayerProfileId, CanonicalCharacterId);
CREATE INDEX IF NOT EXISTS IX_PlayerProfileMemories_Profile ON PlayerProfileMemories(PlayerProfileId, CanonicalCharacterId);
CREATE INDEX IF NOT EXISTS IX_PlayerProfileSaveLinks_SaveFileName ON PlayerProfileSaveLinks(SaveFileName);

CREATE INDEX IF NOT EXISTS IX_Characters_Name ON Characters(Name);
CREATE INDEX IF NOT EXISTS IX_Characters_CanonicalCharacterId ON Characters(CanonicalCharacterId);
CREATE INDEX IF NOT EXISTS IX_Characters_IsActive ON Characters(IsActive);
CREATE INDEX IF NOT EXISTS IX_Characters_SourceModId ON Characters(SourceModId);
CREATE INDEX IF NOT EXISTS IX_Characters_Fingerprint ON Characters(CharacterFingerprint);
CREATE UNIQUE INDEX IF NOT EXISTS UX_Characters_Source_Name ON Characters(SourceModId, Name);
CREATE UNIQUE INDEX IF NOT EXISTS UX_CharacterSources_Canonical_Source_Type ON CharacterSources(CanonicalCharacterId, SourceModId, SourceType);
CREATE INDEX IF NOT EXISTS IX_CharacterMergeRules_Name ON CharacterMergeRules(MatchName, MatchSourceModId, MatchInternalName);
CREATE UNIQUE INDEX IF NOT EXISTS UX_CharacterAliases_Canonical_Alias_Source ON CharacterAliases(CanonicalCharacterId, Alias, SourceModId);
CREATE INDEX IF NOT EXISTS IX_CharacterMergeReviewQueue_Status ON CharacterMergeReviewQueue(Status, Confidence);
CREATE INDEX IF NOT EXISTS IX_Relationships_CharacterA ON Relationships(CharacterA);
CREATE INDEX IF NOT EXISTS IX_Relationships_CharacterB ON Relationships(CharacterB);
CREATE INDEX IF NOT EXISTS IX_Memories_CharacterId_CreatedDate ON Memories(CharacterId, CreatedDate);
CREATE INDEX IF NOT EXISTS IX_VoiceRules_CharacterId ON VoiceRules(CharacterId);
CREATE UNIQUE INDEX IF NOT EXISTS UX_DialogueExamples_Character_Text ON DialogueExamples(CharacterId, DialogueText);
CREATE INDEX IF NOT EXISTS IX_GeneratedDialogueHistory_Character_CreatedDate ON GeneratedDialogueHistory(CharacterId, CreatedDate);
CREATE UNIQUE INDEX IF NOT EXISTS UX_DialogueSources_Source_Key ON DialogueSources(CanonicalCharacterId, SourceModId, FilePath, DialogueKey);
CREATE INDEX IF NOT EXISTS IX_DialogueSources_Canonical_Active ON DialogueSources(CanonicalCharacterId, IsActive, SourcePriority);
CREATE INDEX IF NOT EXISTS IX_GeneratedDialogueOverrides_Canonical ON GeneratedDialogueOverrides(CanonicalCharacterId, IsApproved, IsEnabled);
CREATE INDEX IF NOT EXISTS IX_CharacterHistory_CharacterId_Timestamp ON CharacterHistory(CharacterId, Timestamp);
CREATE INDEX IF NOT EXISTS IX_LoreChangeLog_CharacterId_Timestamp ON LoreChangeLog(CharacterId, Timestamp);
CREATE INDEX IF NOT EXISTS IX_UserLoreOverrides_CharacterId ON UserLoreOverrides(CharacterId);
CREATE INDEX IF NOT EXISTS IX_LoreConflicts_Reviewed ON LoreConflicts(IsReviewed, CreatedDate);
CREATE INDEX IF NOT EXISTS IX_ScanHistory_StartedAt ON ScanHistory(StartedAt);
CREATE UNIQUE INDEX IF NOT EXISTS UX_CharacterValidation_Mod_Name ON CharacterValidationResults(SourceModId, Name);
CREATE INDEX IF NOT EXISTS IX_CharacterValidation_Classification ON CharacterValidationResults(Classification, Score);
