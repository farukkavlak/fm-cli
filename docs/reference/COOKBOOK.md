# FM26 reçete kitabı

Oyunun aksiyon ve veri yüzeyi, **deneyerek değil metadata'dan** çıkarılmış. Buradaki hiçbir
şey tahmin değil; her satırın kaynağı `enums.txt`, `actions.txt`, `events.txt` veya
`data-model.txt`.

## 0. Bir aksiyonu nasıl gönderiyoruz

İki katman var ve ikisi de kataloglu.

**UI katmanı** — `EventBase<T>`'den türeyen 275 tip (`events.txt`). Alanları düz; `/send` ile
isimle gönderiliyor:

```
/send?event=<TypeName>&<Alan>=<değer|binding-yolu|EnumÜyeAdı>[&dry=1]
```

Alan tipleri: sayı/bool/string doğrudan, **referans** binding yolundan, **enum** üye adından,
**`TypedValue`** scalar'dan, **`PanelID`** ekran adından. `dry=1` payload'ı kurup raporlar ama
göndermez — yeni bir aksiyonu kayda dokunmadan denemenin yolu. Bir alan çözülemezse event hiç
gönderilmez.

**Simülasyon katmanı** — `FM.GamePlugin.InteropEventIDs`, **680 event** (`actions.txt`).
Bunlar oyunun kendi motoruna giden aksiyonlar: basın toplantısında soru cevaplamak, transfer
teklifine yanıt vermek, taktik direktifi değiştirmek.

İkisi arasındaki köprü doğrulandı (§8.15): bir UI aksiyon kaydındaki `Event` alanı **doğrudan
bir `InteropEventIDs` değeri**:

```
Action.Event = 27  →  InteropEventIDs.EventFmxNewsResponseAction
```

Yani bir ekrandaki herhangi bir butonun ne gönderdiğini `/dyn` ile okuyup adını
`actions.txt`'ten bulabiliyoruz. Tersi de geçerli: aksiyonun adını biliyorsak hangi ID'yi
göndereceğimizi biliyoruz.

`FM.GamePlugin.EventToPlugin` bu katmanın genel taşıyıcısı:

```csharp
EventToPlugin { InteropReference Target; InteropEventIDs ID; TypedValue DataToSend; }
```

Ayrıca `FM.GamePlugin.ActionID` — **225 isimli UI aksiyonu** (`PersonMakeOfferForPlayer`,
`PersonOfferContract`, `ClubRequestIncreaseBudgets`…). `GameInteropSubsystem.m_handlers`
bunları `Dictionary<ActionID, …>` olarak tutuyor.

**En kısa yol: kontrolün kendisine bas.** Ekrandaki her kontrol göndereceği event'i ve
alanlarının hangi binding yolundan geldiğini taşıyor (`docs/FINDINGS.md` §8.25):

```
/buttons[?type=<metin>][&text=<etiket>]      yüklü ekranın kontrolleri, event'leriyle
/press?ptr=<adres>[&list=<ad>][&dry=1]       kontrolün event listesini kuyruğa koy
```

Payload'ı oyun dolduruyor. `/send` hâlâ ekranı olmayan ya da ekranına gitmek istemediğimiz
event'ler için duruyor.

---

## 1. Taktik — FM26'nın iki fazı

FM26'nın yeniliği olan hücum/savunma ayrımı enum düzeyinde net:

| Enum | Üye | Ne |
|---|---|---|
| `FM.UI.InPossessionRole` | 57 | Topla oynarken roller |
| `FM.UI.OutOfPossessionRole` | 46 | Top rakipteyken roller (`OP` önekli) |
| `FM.UI.TeamInstructionPhaseID` | 14 | Faz: BuildUp, Progression, FinalThird / HighPress, MidBlock, LowBlock… |
| `FM.UI.TacticTeamInstructions` | 33 | Takım talimatı eksenleri |
| `FM.UI.TacticMentalities` | 8 | VeryDefensive → VeryAttacking |
| `FM.UI.TacticalStyles` | 13 | Gegenpress, TikiTaka, Catenaccio… |
| `FM.UI.PlayerInstruction` | 36 | Oyuncu direktifleri (`PI_` önekli) |
| `FM.UI.Formation` | 72 | Formasyonlar |

Örnekler (tam liste `enums.txt`):

```
InPossessionRole     InvertedFullBack, WideCentreBack, OverlappingCentreBack, Mezzala,
                     SegundoVolante, Regista, Trequartista, ChannelMidfielder
OutOfPossessionRole  OPPressingFullBack, OPHoldingWingBack, OPScreeningCentralMidfielder,
                     OPInvertingOutletWinger, OPTrackingCentreForward
TeamInstructionPhase InPossessionDefault, BuildUp, Progression, FinalThird, SupportingRuns,
                     Dribbling, ProgressThrough, GKDistributeTo
                     OutOfPossessionDefault, HighPress, MidBlock, LowBlock, PressingTrap
PlayerInstruction    PI_CUT_INSIDE_WITH_BALL, PI_MOVE_INTO_CHANNELS, PI_MARK_SPECIFIC_PLAYER,
                     PI_HIGHER_PRESSING_INTENSITY, PI_HOLD_UP_BALL
```

⚠ `InPossessionRole` değerleri **bit maskesi** (1, 2, 4, … 144115188075855872), yani bir
pozisyonun uygun rolleri tek sayıda toplanabiliyor. `OutOfPossessionRole` da öyle.
`TeamInstructionPhaseID` ve `TacticMentalities` düz sayı.

**Veri:** `TacticReference` (207 property), `TeamTacticReference` (207),
`MatchTacticReference` (227), `CompetitionTacticReference` (207). İlgili property'ler:
`IPRole`, `InPossessionInstructionList`, `Mentality`, `MentalityDropdown`, `TacticalStyle`,
`Formation`, `FormationName`, `CurrentRoleDropdown`, `AvailableRolesDropdown`,
`KeyAttributesForRole`, `NextOppositionInstructions`, `CrossingStyle`.

**Event'ler:**

```csharp
ModifyTacticEvent { InteropReference Target; TypedValue Data; TypedValue SecondaryData;
                    TypedValue Team; bool IsAddingSlot }
TacticTeamSelectionDropdownEvent { ..., TypedValue Position, TypedValue IsInPossession,
                                   TypedValue TeamSelectionIndex }
```

`IsInPossession` alanının event seviyesinde olması, iki fazın aynı event'le ama farklı
bayrakla sürüldüğünü gösteriyor.

İlgili `InteropEventIDs`: `EventFmxMatchTacticsChanged`, `EventFmxSetTacticSlot`,
`EventFmxSetPresetTactic`, `EventFmxDuplicateTactic`, `EventFmxRemoveTactic`,
`EventFmxSetPrimaryTrainingTacticSlot`, `EventFmxQuickChangeTacticInstructionIncrement` /
`Decrement`, `EventFmxQuickChangePlayerInstructionIncrement` / `Decrement`,
`EventFmxOppositionInstructionsClear`, `EventFmxOppositionInstructionsApplyAdvice`.

`ModifyTacticOption` enum'u da taktik yönetimi menüsünü veriyor: `CREATE_NEW`,
`DUPLICATE_SLOT`, `EDIT_TACTIC`, `PRESETS`, `SAVE`, `LOAD`, `MANAGE`, `CLEAR`,
`PRIMARY_TRAINED_TACTIC`.

---

## 2. Transfer ve sözleşme

**Veri:** `TransferOfferEditSessionReference` (99 property) — bir teklif pazarlığının tamamı:

```
CanMakeTransferOffer      CanMakeLoanOffer         CanMakeChangesToOffer
ExchangePlayersList       ExchangePotentialPlayersList   ExchangePlayersCanAddPlayer
LoanToBuyVisible          LoanToBuyHasFeeSet       LoanListPlayer
DeadlineDateOptionList    DeadlineDateOptionValue  JoinDate
GuaranteedCostBreakdownStringList   Negotiable     HasTransferAvailabilityWarning
BuyingTeam                Agent                    Person        ClubMessage
```

Ayrıca `ContractEditSessionReference` (80), `ContractReference` (124),
`ContractEditSessionAddOnComponentReference` (13), `ClubTransferClausesReference` (12).

**`ActionID` aksiyonları:**

```
PersonMakeOfferForPlayer          PersonOfferContract         PersonOfferVirtualContract
PersonWithdrawContractOffer       PersonOfferToClubs          PersonImmediateTransfer
PersonOfferTrialToPlayer          PersonOfferExtendLoan       PersonLoanForOneMatch
PersonMutualTerminationContract   PersonTerminateContract     PersonReleaseClubFreeTransfer
PersonAddPlayersAsTransferTargets PersonRemovePlayersAsTransferTargets
PersonAskAgentAboutClientContractRenewal   PersonAskAgentToOfferClientToClubs
PersonAskIntermediariesToOfferPlayerToClubs
PersuadePlayerAgentToDisregardFailedContractNegotiations
ClubBudgetAdjustmentTransfers     ClubBudgetAdjustmentWages   ClubRequestIncreaseBudgets
```

**`InteropEventIDs`:** `EventFmxTransferOfferResponse`,
`EventFmxTransferOfferSelectOfferType`, `EventFmxTransferOfferSetProposalClubsCancel`,
`EventFmxTransferRoomAddAdvertConfirm` / `Cancel`, `EventFmxTransferRoomDeleteAdvert`,
`EventFmxProceedToContractNegotiations`, `EventFmxUseExistingContract`,
`EventFmxAddContractBonus`, `EventFmxAddPreContractPromise`,
`EventFmxSetContractAddOnExpiryDate`.

---

## 3. Basın toplantısı

**Veri:** `PressConferenceEditSessionReference` (10 property) — toplantının akışı:

```
HasPressConferenceStarted   HasPressConferenceFinished
JournalistSpeaking -> InteractionPersonAndBodyLanguage
JournalistsAttending        NumberOfJournalistsAttending
NumberOfQuestionsAsked      TotalNumberOfQuestions      PercentageOfQuestionsAsked
ConversationRoomAtmosphere -> InteractionBodyLanguage
SpeakTo -> SpeakTo
```

**`InteropEventIDs`:**

```
EventFmxStartPressConference       EventFmxPressConferenceAnswerQuestion
EventFmxPressConferenceNoComment   EventFmxPressConferenceEarlyExit
EventFmxPressConferenceStormOut    EventFmxLeakInteractionTopicToPress
```

Yani basın toplantısı bizim için gelen kutusuyla aynı şekle sahip: bir oturum referansı +
cevap seçenekleri + seçimi gönderen bir event. Mesaj cevaplama (§8.15) çalıştığına göre bu da
aynı desenle çözülmeli.

---

## 4. Oyuncu/personel konuşmaları

**Veri:** `ConversationEditSessionReference` (24 property):

```
ConversationSubject          ConversationInteractees        ConversationSpectators
ConversationRoomAtmosphere -> InteractionBodyLanguage       ConversationShowBodyLanguage
ConversationSummaryPromises  ConversationSummaryPlayerTargets
ConversationSummaryActions   ConversationSummaryNotableHighlights
ConversationQuickChatSummaryText   ConversationTopicDescriptionString
ConversationCanSetTrainingTargets  SpeakTo -> SpeakTo
```

`ConversationEditSessionSelectionDialogReference` (5) seçim diyaloğunu taşıyor
(`ConversationValueSelectionDataPossibleValues`, `…CurrentValue`, `…UsesCashValue`).

**Event:** `InteropEventIDs.EventFmxConversationReplySelected`.
İlgili `ActionID`: `PersonPrivateNationalConversation`.

`SpeakToReference` (12) bütün konuşmaların ortak girişi: `Actions -> speaktoactions`,
`SpeakToMode`, `InteractionIsComplete`, `SpeakToMostRecentResponseIndex`,
`CurrentInductionStepIndex` / `MaxInductionStepIndex`.

---

## 5. Antrenman

**`ActionID`:** `PersonRequestTrainingReport`, `PersonRestTrainingSchedule`,
`PersonCancelRestTrainingSchedule`, `PersonTrainingNewPreferredMove`,
`PersonTrainingNewPosition`, `PersonTogglePlayerFirstTeamTraining`.

**`InteropEventIDs`:** `EventFmxPlayerTrainingIntensityLevelChanged`,
`EventFmxPlayerTrainingFocusChanged`, `EventFmxConfirmTrainingFocusChanges`,
`EventFmxRevertTrainingFocusChanges`, `EventFmxAutoSelectTrainingFocus`.

**Veri:** `CustomTrainingScheduleReference` (17), `DailyScheduleReference`.

---

## 6. Kadro planlama

`InteropEventIDs`: `EventFmxClearSquad`, `EventFmxSquadPlannerAddPlayer`,
`EventFmxSquadPlannerRemovePlayer`, `EventFmxSquadPlannerMovePlayer`,
`EventFmxSquadPlannerDragPlayer`, `EventFmxSquadPlannerAskAssistant`.

---

## 7. Maç

`InteropEventIDs`: `EventFmxGoToMatch`, `EventFmxExitMatch`, `EventFmxMatchTacticsChanged`,
`EventFmxMatchSave`, `EventFmxSetMatchRules`, `EventFmxMatchFocusSelectFirstSetPiece`.
`MatchReference` 206 property taşıyor.

---

## Kaynak dosyalar

| Dosya | İçerik |
|---|---|
| `data-model.txt` | 931 referans tipi, 14504 isimli property, referans kenarlarıyla |
| `events.txt` | 275 UI event tipi, alanlarıyla |
| `actions.txt` | 225 `ActionID` + 680 `InteropEventIDs`, isim sırasında |
| `enums.txt` | 2160 enum, 19119 üye |
| `panels.txt` | yüklü ekranlar, çevrilmiş isimleriyle (kısmi) |

Yenileme: `/schema`, `/panels` (oyun açıkken) ve
`sigdump --catalog events` / `--enums` / `--method <ad>` (oyun gerekmez).
