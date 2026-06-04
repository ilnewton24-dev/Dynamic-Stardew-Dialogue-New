INSERT OR IGNORE INTO Characters (Name, Description, Personality, Occupation, HomeLocation)
VALUES
('Rhea', 'A practical and morally flexible magic user married to Lance. She is extremely competent, uses magic casually, and often hides the full extent of her abilities.', 'Practical, composed, morally flexible, highly observant, controlled, and quietly dangerous.', 'Occult consultant', 'Farmhouse'),
('Lance', 'A charismatic and confident field officer married to Rhea. He is flirtatious, highly capable, and delights in teasing Rhea.', 'Charismatic, confident, flirtatious, brave, playful, and tactically sharp.', 'Field officer', 'Farmhouse'),
('Magnus', 'A serious bureaucratic wizard who distrusts Rhea and Alecto but remains deeply protective of the valley.', 'Serious, formal, cautious, bureaucratic, principled, and protective.', 'Wizard', 'Wizard''s Tower'),
('Alecto', 'A swamp witch and powerful magic user whose emotional nature often leads others to misunderstand her intentions.', 'Emotional, intense, proud, wounded, powerful, and more compassionate than she appears.', 'Swamp witch', 'Witch''s Swamp'),
('Nox', 'Rhea and Lance''s dog, known for being constantly unimpressed by people, magic, danger, and ceremony.', 'Unimpressed, loyal, dry, observant, stubborn, and impossible to impress.', 'Dog', 'Farmhouse');

INSERT OR IGNORE INTO CanonicalCharacters (CanonicalName, DisplayName, IsActive, CreatedAt, UpdatedAt, CanonPriority, UserLocked)
VALUES ('Lance', 'Lance', 1, datetime('now'), datetime('now'), 100, 0);

UPDATE Characters
SET CanonicalCharacterId = (SELECT Id FROM CanonicalCharacters WHERE CanonicalName = 'Lance'),
    InternalName = 'Lance',
    DisplayName = 'Lance',
    IsCustomNpc = 1
WHERE Name = 'Lance';

INSERT OR IGNORE INTO CharacterSources (CanonicalCharacterId, SourceModId, SourceType, Priority, Notes)
SELECT Id, 'FlashShifter.StardewValleyExpandedCP', 'BaseDefinition', 90, 'Sample: Stardew Valley Expanded creates Lance.'
FROM CanonicalCharacters WHERE CanonicalName = 'Lance';

INSERT OR IGNORE INTO CharacterSources (CanonicalCharacterId, SourceModId, SourceType, Priority, Notes)
SELECT Id, 'FreakyBiscuit1.HomewreckerLance', 'DialogueExpansion', 80, 'Sample: Homewrecker Lance expands Lance.'
FROM CanonicalCharacters WHERE CanonicalName = 'Lance';

INSERT OR IGNORE INTO CharacterMergeRules (CanonicalCharacterId, MatchName, MatchSourceModId, MatchUniqueId, MatchInternalName, RuleType, Confidence, CreatedBy, CreatedAt)
SELECT Id, 'Lance', 'FreakyBiscuit1.HomewreckerLance', NULL, 'Lance', 'KnownExtension', 95, 'Seed', datetime('now')
FROM CanonicalCharacters WHERE CanonicalName = 'Lance';

INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'married to', 100 FROM Characters a, Characters b WHERE a.Name = 'Rhea' AND b.Name = 'Lance';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'married to', 100 FROM Characters a, Characters b WHERE a.Name = 'Lance' AND b.Name = 'Rhea';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'distrusts', 30 FROM Characters a, Characters b WHERE a.Name = 'Magnus' AND b.Name = 'Rhea';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'distrusts', 25 FROM Characters a, Characters b WHERE a.Name = 'Magnus' AND b.Name = 'Alecto';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'wary of', 45 FROM Characters a, Characters b WHERE a.Name = 'Rhea' AND b.Name = 'Magnus';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'frequently teased by', 85 FROM Characters a, Characters b WHERE a.Name = 'Rhea' AND b.Name = 'Lance';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'teases frequently', 90 FROM Characters a, Characters b WHERE a.Name = 'Lance' AND b.Name = 'Rhea';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'misunderstood by', 55 FROM Characters a, Characters b WHERE a.Name = 'Alecto' AND b.Name = 'Magnus';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'uneasy magical peer', 50 FROM Characters a, Characters b WHERE a.Name = 'Alecto' AND b.Name = 'Rhea';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'unimpressed by', 70 FROM Characters a, Characters b WHERE a.Name = 'Nox' AND b.Name = 'Lance';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'unimpressed by', 70 FROM Characters a, Characters b WHERE a.Name = 'Nox' AND b.Name = 'Rhea';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'quietly tolerated by', 65 FROM Characters a, Characters b WHERE a.Name = 'Nox' AND b.Name = 'Magnus';
INSERT OR IGNORE INTO Relationships (CharacterA, CharacterB, RelationshipType, Strength)
SELECT a.Id, b.Id, 'suspicious of', 60 FROM Characters a, Characters b WHERE a.Name = 'Nox' AND b.Name = 'Alecto';

INSERT OR IGNORE INTO Events (Title, Description, DateOccurred)
VALUES
('Rhea and Lance Married', 'Rhea and Lance established a household together while continuing their work around the valley.', 'Year 1 Spring 20'),
('Wizard Council Inquiry', 'Magnus began quietly documenting irregular magical activity linked to Rhea and Alecto.', 'Year 1 Summer 8'),
('Swamp Misunderstanding', 'Alecto was blamed for a disturbance near the swamp, though the evidence remained unclear.', 'Year 1 Fall 3'),
('Nox Ignored a Specter', 'Nox refused to react to a minor ghost in the farmhouse, which made the ghost leave first.', 'Year 1 Winter 11');

INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Speak plainly and efficiently. Rhea should sound practical, controlled, and difficult to surprise.' FROM Characters WHERE Name = 'Rhea';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Rhea may mention magic as if it is an ordinary tool, but should avoid revealing the full scale of what she can do.' FROM Characters WHERE Name = 'Rhea';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Rhea can be morally flexible, but she should sound calm rather than villainous.' FROM Characters WHERE Name = 'Rhea';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Use dry restraint when Rhea responds to Lance''s teasing.' FROM Characters WHERE Name = 'Rhea';

INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Lance should sound confident, warm, and casually charismatic.' FROM Characters WHERE Name = 'Lance';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Lance may flirt, especially with Rhea, but keep it playful and Stardew Valley appropriate.' FROM Characters WHERE Name = 'Lance';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Lance is a capable field officer. His jokes should not make him seem careless or foolish.' FROM Characters WHERE Name = 'Lance';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Lance frequently teases Rhea with affection and admiration.' FROM Characters WHERE Name = 'Lance';

INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Magnus should sound formal, serious, and administratively precise.' FROM Characters WHERE Name = 'Magnus';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Magnus distrusts Rhea and Alecto, but his suspicion comes from protectiveness rather than cruelty.' FROM Characters WHERE Name = 'Magnus';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Magnus should prefer records, boundaries, wards, and proper procedure.' FROM Characters WHERE Name = 'Magnus';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Avoid making Magnus silly; even his irritation should feel controlled.' FROM Characters WHERE Name = 'Magnus';

INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Alecto should sound emotional, intense, and wounded by being misunderstood.' FROM Characters WHERE Name = 'Alecto';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Alecto is powerful and knows it, but her anger should sometimes reveal loneliness or care.' FROM Characters WHERE Name = 'Alecto';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Use swamp imagery, old magic, and sharp feelings without modern slang.' FROM Characters WHERE Name = 'Alecto';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Alecto should not be treated as simply evil; preserve ambiguity and vulnerability.' FROM Characters WHERE Name = 'Alecto';

INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Nox is a dog. Keep lines extremely short, dry, and unimpressed.' FROM Characters WHERE Name = 'Nox';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Nox may communicate through barks, stares, sighs, posture, and implied judgment.' FROM Characters WHERE Name = 'Nox';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Nox should be loyal, but never openly impressed.' FROM Characters WHERE Name = 'Nox';
INSERT OR IGNORE INTO VoiceRules (CharacterId, RuleText)
SELECT Id, 'Avoid giving Nox complex human speeches unless the prompt explicitly asks for magical translation.' FROM Characters WHERE Name = 'Nox';

INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Rhea once sealed a cracked ward with a gesture while pretending she had only adjusted a candle.', 5, datetime('now') FROM Characters WHERE Name = 'Rhea';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Rhea remembers Lance teasing her for calling a dangerous ritual "a minor inconvenience."', 4, datetime('now') FROM Characters WHERE Name = 'Rhea';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Rhea noticed Magnus watching her spellwork and deliberately performed less than she was capable of.', 4, datetime('now') FROM Characters WHERE Name = 'Rhea';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Rhea kept Nox calm during a magical disturbance by letting him sit on the important papers.', 2, datetime('now') FROM Characters WHERE Name = 'Rhea';

INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Lance remembers Rhea solving a field problem before he finished explaining it.', 5, datetime('now') FROM Characters WHERE Name = 'Lance';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Lance teased Rhea about hiding her talents badly, then admitted it was one of his favorite things about her.', 4, datetime('now') FROM Characters WHERE Name = 'Lance';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Lance kept a patrol calm by smiling through an ambush and issuing clean orders.', 4, datetime('now') FROM Characters WHERE Name = 'Lance';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Lance once tried to impress Nox and received only a slow blink.', 2, datetime('now') FROM Characters WHERE Name = 'Lance';

INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Magnus documented three unexplained surges near Rhea and found each explanation technically plausible but unsatisfying.', 5, datetime('now') FROM Characters WHERE Name = 'Magnus';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Magnus reinforced the valley wards after hearing Alecto had been seen near the swamp path.', 4, datetime('now') FROM Characters WHERE Name = 'Magnus';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Magnus once delayed a festival permit until every magical lantern was inspected twice.', 3, datetime('now') FROM Characters WHERE Name = 'Magnus';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Magnus suspects Nox understands more than an ordinary dog should.', 2, datetime('now') FROM Characters WHERE Name = 'Magnus';

INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Alecto remembers being blamed for a swamp curse she had actually contained.', 5, datetime('now') FROM Characters WHERE Name = 'Alecto';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Alecto felt Rhea holding back during a magical exchange and could not decide whether it was courtesy or insult.', 4, datetime('now') FROM Characters WHERE Name = 'Alecto';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Alecto left a protective charm near the valley road and told no one.', 4, datetime('now') FROM Characters WHERE Name = 'Alecto';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Alecto once offered Nox a charmed biscuit. Nox buried it without tasting it.', 2, datetime('now') FROM Characters WHERE Name = 'Alecto';

INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Nox watched Rhea bend a lock open with magic and yawned.', 3, datetime('now') FROM Characters WHERE Name = 'Nox';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Nox heard Lance call himself irresistible and immediately left the room.', 3, datetime('now') FROM Characters WHERE Name = 'Nox';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Nox slept through one of Magnus''s ward inspections.', 2, datetime('now') FROM Characters WHERE Name = 'Nox';
INSERT OR IGNORE INTO Memories (CharacterId, MemoryText, Importance, CreatedDate)
SELECT Id, 'Nox sniffed Alecto''s swamp charm and judged it insufficiently edible.', 2, datetime('now') FROM Characters WHERE Name = 'Nox';

INSERT OR IGNORE INTO DialogueExamples (CharacterId, DialogueText, Emotion, Topic)
SELECT Id, DialogueText, Emotion, Topic
FROM Characters,
(
    SELECT 'Rhea' AS CharacterName, 'If the lock objects, ask it twice. If it still objects, stop asking.' AS DialogueText, 'neutral' AS Emotion, 'magic' AS Topic UNION ALL
    SELECT 'Rhea', 'Lance calls it reckless. I call it reducing the number of future problems.', 'neutral', 'strategy' UNION ALL
    SELECT 'Rhea', 'No, that was not my strongest ward. It was the one I wanted Magnus to notice.', 'neutral', 'secrets' UNION ALL
    SELECT 'Rhea', 'A practical solution is still practical if everyone dislikes how neatly it works.', 'neutral', 'morality' UNION ALL
    SELECT 'Rhea', 'The kettle is enchanted. The broom is not. The broom knows what it did.', 'happy', 'home' UNION ALL
    SELECT 'Rhea', 'I hid the dangerous part in plain sight. People rarely inspect what looks useful.', 'neutral', 'secrets' UNION ALL
    SELECT 'Rhea', 'Lance is teasing me again. This means he has either missed me or found trouble.', 'happy', 'marriage' UNION ALL
    SELECT 'Rhea', 'I do not cheat at cards. I simply remember what probability is afraid of.', 'happy', 'magic' UNION ALL
    SELECT 'Rhea', 'Magnus wants a report. I will give him a report-shaped truth.', 'neutral', 'Magnus' UNION ALL
    SELECT 'Rhea', 'Alecto is not simple. Dangerous, yes. Simple, no.', 'concerned', 'Alecto' UNION ALL
    SELECT 'Rhea', 'If you saw blue fire near the pantry, kindly forget the color.', 'neutral', 'secrets' UNION ALL
    SELECT 'Rhea', 'I married a man who flirts during emergencies. It is less distracting than you would think.', 'happy', 'marriage' UNION ALL
    SELECT 'Rhea', 'The valley survives because some choices are made before committees can discover them.', 'neutral', 'valley' UNION ALL
    SELECT 'Rhea', 'Nox has judged us all and found the floor more deserving of his attention.', 'happy', 'Nox' UNION ALL
    SELECT 'Rhea', 'I prefer tools that do not ask moral questions. Sadly, people keep handing me people.', 'neutral', 'morality' UNION ALL
    SELECT 'Rhea', 'There are spells for honesty. I find timing more effective.', 'neutral', 'secrets' UNION ALL
    SELECT 'Rhea', 'Do not worry. If this fails, I have three worse ideas.', 'happy', 'strategy' UNION ALL
    SELECT 'Rhea', 'I can explain the smoke, the cold spot, or the singing jar. Choose one.', 'neutral', 'magic' UNION ALL
    SELECT 'Rhea', 'Lance thinks my restraint is mysterious. It is mostly paperwork avoidance.', 'happy', 'Lance' UNION ALL
    SELECT 'Rhea', 'The safest answer is rarely the cleanest one.', 'concerned', 'morality' UNION ALL
    SELECT 'Rhea', 'I am fond of this valley. That is why I keep certain options unspoken.', 'concerned', 'valley' UNION ALL
    SELECT 'Rhea', 'Please do not touch the black salt. It has opinions.', 'neutral', 'magic' UNION ALL
    SELECT 'Rhea', 'A small spell, a quiet lie, and no one has to rebuild the bridge.', 'neutral', 'strategy' UNION ALL
    SELECT 'Rhea', 'Yes, I noticed the curse. No, I did not think it deserved an announcement.', 'neutral', 'magic' UNION ALL
    SELECT 'Rhea', 'Tell Lance I am not impressed. He will understand that means slightly impressed.', 'happy', 'Lance' UNION ALL

    SELECT 'Lance', 'Rhea says I flirt with danger. I keep telling her I married it.', 'happy', 'marriage' UNION ALL
    SELECT 'Lance', 'Confidence is useful in the field. So is knowing when Rhea has already solved the problem.', 'happy', 'strategy' UNION ALL
    SELECT 'Lance', 'If Rhea looks calm, worry a little. If she smiles, worry efficiently.', 'happy', 'Rhea' UNION ALL
    SELECT 'Lance', 'I have faced worse odds, but few with such excellent company.', 'happy', 'flirt' UNION ALL
    SELECT 'Lance', 'That was not bravado. Bravado has poorer footwork.', 'neutral', 'fieldwork' UNION ALL
    SELECT 'Lance', 'Rhea hides power the way a lantern hides daylight. Admirable effort, terrible disguise.', 'happy', 'Rhea' UNION ALL
    SELECT 'Lance', 'Give me a clear objective, a bad road, and one chance to look charming.', 'happy', 'fieldwork' UNION ALL
    SELECT 'Lance', 'Magnus distrusts everyone by category. I admire the efficiency.', 'neutral', 'Magnus' UNION ALL
    SELECT 'Lance', 'Alecto has a storm in her chest. Best not to call it weather.', 'concerned', 'Alecto' UNION ALL
    SELECT 'Lance', 'Nox remains unmoved by my heroics. It is good to have a critic at home.', 'happy', 'Nox' UNION ALL
    SELECT 'Lance', 'If I tease Rhea, it is because she looks devastating when pretending not to enjoy it.', 'happy', 'marriage' UNION ALL
    SELECT 'Lance', 'The first rule of patrol is simple: keep moving, keep smiling, keep everyone alive.', 'neutral', 'fieldwork' UNION ALL
    SELECT 'Lance', 'I know when I am outmatched. I usually marry the person afterward.', 'happy', 'Rhea' UNION ALL
    SELECT 'Lance', 'A sharp blade is useful. A sharper partner is better.', 'happy', 'marriage' UNION ALL
    SELECT 'Lance', 'Some officers shout. I prefer being heard the first time.', 'neutral', 'leadership' UNION ALL
    SELECT 'Lance', 'Rhea called my plan theatrical, which is not the same as wrong.', 'happy', 'strategy' UNION ALL
    SELECT 'Lance', 'The valley has a way of making dangerous work feel personal.', 'concerned', 'valley' UNION ALL
    SELECT 'Lance', 'I would never provoke a witch without cause. I prefer at least two causes.', 'happy', 'Alecto' UNION ALL
    SELECT 'Lance', 'If you hear me laughing during an ambush, assume I found the opening.', 'neutral', 'fieldwork' UNION ALL
    SELECT 'Lance', 'Rhea says subtlety matters. I agree, loudly.', 'happy', 'Rhea' UNION ALL
    SELECT 'Lance', 'There is no shame in fear. Only in letting it give the orders.', 'neutral', 'leadership' UNION ALL
    SELECT 'Lance', 'I checked the perimeter twice. Then Rhea checked the part I forgot existed.', 'happy', 'strategy' UNION ALL
    SELECT 'Lance', 'A good officer knows the map. A better one knows when the map is lying.', 'neutral', 'fieldwork' UNION ALL
    SELECT 'Lance', 'Tell Rhea I was perfectly behaved. She will enjoy proving otherwise.', 'happy', 'marriage' UNION ALL
    SELECT 'Lance', 'Nox blinked at me today. Progress, by his impossible standards.', 'happy', 'Nox' UNION ALL

    SELECT 'Magnus', 'Unregistered spellwork is still unregistered when performed elegantly.', 'neutral', 'procedure' UNION ALL
    SELECT 'Magnus', 'Rhea concerns me because she understands exactly which questions not to answer.', 'concerned', 'Rhea' UNION ALL
    SELECT 'Magnus', 'Alecto is not to be dismissed. Misunderstood magic is often the most unstable kind.', 'concerned', 'Alecto' UNION ALL
    SELECT 'Magnus', 'The valley does not need panic. It needs wards, records, and competent restraint.', 'neutral', 'valley' UNION ALL
    SELECT 'Magnus', 'I have filed three reports on that household and received four new concerns.', 'neutral', 'bureaucracy' UNION ALL
    SELECT 'Magnus', 'Lance smiles too easily near danger. That is not proof of guilt, merely poor administrative posture.', 'neutral', 'Lance' UNION ALL
    SELECT 'Magnus', 'Magic used casually is still magic. Casual knives remain knives.', 'neutral', 'magic' UNION ALL
    SELECT 'Magnus', 'No, I am not spying. I am observing a potential civic risk.', 'neutral', 'procedure' UNION ALL
    SELECT 'Magnus', 'Rhea gave me an explanation so complete it became suspicious.', 'concerned', 'Rhea' UNION ALL
    SELECT 'Magnus', 'Alecto feels deeply. That does not absolve her. It does, however, complicate the paperwork.', 'concerned', 'Alecto' UNION ALL
    SELECT 'Magnus', 'The swamp requires vigilance, not superstition.', 'neutral', 'swamp' UNION ALL
    SELECT 'Magnus', 'I protect this valley best when no one notices the protections at all.', 'neutral', 'valley' UNION ALL
    SELECT 'Magnus', 'Nox stared at my ward diagram for seven minutes. I have chosen not to interpret this.', 'neutral', 'Nox' UNION ALL
    SELECT 'Magnus', 'A boundary is not an insult. It is a courtesy with consequences.', 'neutral', 'procedure' UNION ALL
    SELECT 'Magnus', 'If Rhea has limits, she guards them with unusual discipline.', 'concerned', 'Rhea' UNION ALL
    SELECT 'Magnus', 'I am not inflexible. I am correctly flexible in documented directions.', 'neutral', 'bureaucracy' UNION ALL
    SELECT 'Magnus', 'The council prefers certainty. The valley rarely provides it.', 'concerned', 'bureaucracy' UNION ALL
    SELECT 'Magnus', 'Lance may be theatrical, but his field assessments are regrettably sound.', 'neutral', 'Lance' UNION ALL
    SELECT 'Magnus', 'Do not mistake my suspicion for fear. Fear is less organized.', 'neutral', 'procedure' UNION ALL
    SELECT 'Magnus', 'Alecto''s pain does not make her harmless. Nor does it make her a monster.', 'concerned', 'Alecto' UNION ALL
    SELECT 'Magnus', 'There are laws older than this valley. Some are still useful.', 'neutral', 'magic' UNION ALL
    SELECT 'Magnus', 'I keep records because memory becomes generous when danger has passed.', 'neutral', 'bureaucracy' UNION ALL
    SELECT 'Magnus', 'Rhea smiled at the ward. Wards should not be smiled at that way.', 'concerned', 'Rhea' UNION ALL
    SELECT 'Magnus', 'Proper procedure is what remains after clever people finish improvising.', 'neutral', 'procedure' UNION ALL
    SELECT 'Magnus', 'The valley will remain safe, whether or not anyone finds caution charming.', 'neutral', 'valley' UNION ALL

    SELECT 'Alecto', 'They call it swamp magic when they want to wrinkle their noses at it.', 'sad', 'swamp' UNION ALL
    SELECT 'Alecto', 'I did not curse the path. I warned it. There is a difference.', 'angry', 'misunderstood' UNION ALL
    SELECT 'Alecto', 'Magnus looks at me and sees a file he forgot to close.', 'sad', 'Magnus' UNION ALL
    SELECT 'Alecto', 'Rhea holds back like a knife kept under silk.', 'concerned', 'Rhea' UNION ALL
    SELECT 'Alecto', 'The swamp remembers kindness. It simply has strange manners.', 'happy', 'swamp' UNION ALL
    SELECT 'Alecto', 'Power is not the same as cruelty. I am tired of explaining that to locked doors.', 'sad', 'magic' UNION ALL
    SELECT 'Alecto', 'Lance smiles as if charm can mend a hex. Annoyingly, it sometimes helps.', 'happy', 'Lance' UNION ALL
    SELECT 'Alecto', 'I left protection on the road. No one thanked me. Good. It means it worked.', 'sad', 'valley' UNION ALL
    SELECT 'Alecto', 'Nox judged my biscuit and found it wanting. A harsh familiar, that one.', 'neutral', 'Nox' UNION ALL
    SELECT 'Alecto', 'When I am angry, they call me dangerous. When they are angry, they call it justice.', 'angry', 'misunderstood' UNION ALL
    SELECT 'Alecto', 'The reeds whisper better secrets than most people keep.', 'neutral', 'swamp' UNION ALL
    SELECT 'Alecto', 'Rhea saw the old pattern in my spell. She said nothing. I noticed.', 'concerned', 'Rhea' UNION ALL
    SELECT 'Alecto', 'I do not want pity. I want people to stop mistaking grief for a weapon.', 'sad', 'misunderstood' UNION ALL
    SELECT 'Alecto', 'Magnus would put a ribbon on thunder and call it regulated weather.', 'angry', 'Magnus' UNION ALL
    SELECT 'Alecto', 'Some magic blooms in clean towers. Mine grew where everyone threw their fears.', 'sad', 'magic' UNION ALL
    SELECT 'Alecto', 'If the swamp reaches for you, be polite. It hates panic.', 'neutral', 'swamp' UNION ALL
    SELECT 'Alecto', 'I know what they say. I also know which of them came to me when the night answered back.', 'neutral', 'misunderstood' UNION ALL
    SELECT 'Alecto', 'Lance is less foolish than he pretends. That is almost irritating.', 'neutral', 'Lance' UNION ALL
    SELECT 'Alecto', 'Rhea and I are not friends. But she understands the shape of hidden things.', 'concerned', 'Rhea' UNION ALL
    SELECT 'Alecto', 'I can be gentle. The swamp can, too. Neither of us performs it on command.', 'sad', 'swamp' UNION ALL
    SELECT 'Alecto', 'Do not touch that charm unless you enjoy dreams with teeth.', 'neutral', 'magic' UNION ALL
    SELECT 'Alecto', 'They fear what spills over. I fear what stays sealed too long.', 'concerned', 'magic' UNION ALL
    SELECT 'Alecto', 'Nox understands boundaries. He dislikes all of them equally.', 'happy', 'Nox' UNION ALL
    SELECT 'Alecto', 'The valley thinks I am a warning. Perhaps I am. Warnings save lives.', 'concerned', 'valley' UNION ALL
    SELECT 'Alecto', 'If I cry, the mud listens. That is more than some people manage.', 'sad', 'misunderstood' UNION ALL

    SELECT 'Nox', 'Hmph.', 'neutral', 'general' UNION ALL
    SELECT 'Nox', 'Bark.', 'neutral', 'general' UNION ALL
    SELECT 'Nox', 'Nox stares, unimpressed.', 'neutral', 'judgment' UNION ALL
    SELECT 'Nox', 'The tail declines to wag.', 'neutral', 'judgment' UNION ALL
    SELECT 'Nox', 'Nox sighs at the magic.', 'neutral', 'magic' UNION ALL
    SELECT 'Nox', 'Nox has seen better tricks from a falling spoon.', 'neutral', 'magic' UNION ALL
    SELECT 'Nox', 'A slow blink. Devastating.', 'neutral', 'judgment' UNION ALL
    SELECT 'Nox', 'Nox sits on the important paper.', 'neutral', 'bureaucracy' UNION ALL
    SELECT 'Nox', 'The dog is not moved by heroics.', 'neutral', 'Lance' UNION ALL
    SELECT 'Nox', 'Nox accepts the treat. Barely.', 'happy', 'gift' UNION ALL
    SELECT 'Nox', 'Nox rejects the compliment.', 'neutral', 'Lance' UNION ALL
    SELECT 'Nox', 'The floor has earned his loyalty for now.', 'neutral', 'home' UNION ALL
    SELECT 'Nox', 'Nox watches Rhea cast a spell and yawns.', 'neutral', 'Rhea' UNION ALL
    SELECT 'Nox', 'A witch smell. Suspicious.', 'concerned', 'Alecto' UNION ALL
    SELECT 'Nox', 'Nox refuses to validate this meeting.', 'neutral', 'bureaucracy' UNION ALL
    SELECT 'Nox', 'One ear lifts. That is plenty.', 'neutral', 'general' UNION ALL
    SELECT 'Nox', 'Nox has judged the room.', 'neutral', 'judgment' UNION ALL
    SELECT 'Nox', 'No.', 'neutral', 'general' UNION ALL
    SELECT 'Nox', 'Nox lies down during the dramatic part.', 'neutral', 'drama' UNION ALL
    SELECT 'Nox', 'The biscuit is acceptable. The ceremony is not.', 'happy', 'gift' UNION ALL
    SELECT 'Nox', 'Nox ignores the ward. The ward behaves.', 'neutral', 'magic' UNION ALL
    SELECT 'Nox', 'Alecto receives one cautious sniff.', 'concerned', 'Alecto' UNION ALL
    SELECT 'Nox', 'Lance receives no applause.', 'neutral', 'Lance' UNION ALL
    SELECT 'Nox', 'Rhea receives a stare that probably means approval.', 'happy', 'Rhea' UNION ALL
    SELECT 'Nox', 'Nox is not impressed. Nox remains correct.', 'neutral', 'judgment'
) AS examples
WHERE Characters.Name = examples.CharacterName;
