# FM27 events the code does not name

From `fm crawl --deep`, English and Turkish (`tools/crawl-report.py`). 104 event names, each with its reason.

## A command works these by the page's own controls

- `coaches`: `EventFmxAssignCoach`, `EventFmxUnassignCoach`, `CheckedEvents`, `UncheckedEvents`
- `takers`, `penalties`, `captains`: `EventFmxSetPieceCaptainsCandidatesMove`,
  `EventFmxSetPiecePenaltyCandidatesMove`, `EventFmxSetPiecePenaltyCandidatesRemove`,
  `EventFmxSetPieceTakerRemoveFromList`, `EventFmxSetPieceToggleChangeIfAlreadyMissed`,
  `EventFmxSetPieceToggleChangeIfAlreadyTaken`, `EventFmxSetPieceToggleTwoGoalsScorers`,
  `TableMoveElement`, `RemoveEvents`
- `setpieces`: `EventFmxSetPieceRoutineDeliverySelect`, `EventFmxSetPieceSelectMenuRoutine`,
  `EventFmxSetPieceStrategyConfirmed`, `SetPieceRoutineOption`
- `planner`: `EventFmxSquadPlannerAddPlayer`, `EventFmxSquadPlannerAskAssistant`, `EventFmxSquadPlannerMovePlayer`,
  `EventFmxSquadPlannerRemovePlayer`
- `tactic`: `EventFmxSwapPlayers`, `EventFmxTeamSelectionClearPlayer`, `EventFmxEnablePositionPersonalisedRole`,
  `EventFmxDisablePositionPersonalisedRole`, `ModifyTactic`, `TacticSelectPlayerForPositionDropdown`
- `players`, `staff find`: `EventFmxAddFilterCondition`, `EventFmxConfirmFilter`, `EventFmxFilterSelectQuickFilter`,
  `EventFmxFilterSelectedExcludeOption`, `EventFmxSearchReset`, `EventFmxSelectFilterMaxValue`,
  `EventFmxSelectFilterMinValue`, `EventFmxSelectFilterValue`, `SelectionEvent`
- `scout new`: `EventFmxRecruitmentFocusIpRoleProficiencyUpdated`, `EventFmxRecruitmentFocusOopRoleProficiencyUpdated`
- `schedule`: `EventFmxSelectTrainingSession`
- `friendly`: `EventFmxSetMatchRules`
- `shortlist`: `EventFmxSelectShortlist`
- `transferroom`: `EventFmxTransferRoomChangeTransferType`, `EventFmxTransferRoomSelectAdvert`,
  `EventFmxDiscardAllPlayersFromAgentsOfferedList`
- `opposition`: `EventFmxOppositionInstructionsApplyAdvice`
- `responsibilities`: `EventFmxSetResponsibilitiesTabToOpen`
- `search`: `SearchResultClicked`

## Nothing to do: the frame, navigation, tutorials

- Moving around: `OpenPanelOrPanel`, `PanelHistoryBack`, `NavigateToSiblingEntity`, `ChangeDropdownSelection`,
  `FocusOnChild`, `FocusSpecificElement`, `ShortcutDpadSimulateButtonClick`, `RadialMenuSetup`, `IGEPanelSetup`,
  `EventFmxSelectPlayer`
- Tutorials: `TriggerTutorial`, `TutorialActionEvents`, `PortalFlow`, `PortalMessages`, `ClubVisionAndConfidence`,
  `TacticalExplainerTutorial`, `TeamReport`
- The game's own page bookkeeping: `EventFmxRemoveAroundTheWorldForcedScreen`, `EventFmxRemoveCompetitionOverviewForcedScreen`,
  `EventFmxRemoveDataHubForcedScreen`, `EventFmxRemoveManagerVacanciesForcedScreen`, `EventFmxRemoveNewsAggregatorForcedScreen`,
  `EventFmxRemoveNewsForYouForcedScreen`, `EventFmxRequestFullAdviceCollection`, `SetNewsActionDataOnPanel`,
  `SwitchNewsActionDataOnPanel`, `MarkMediaNewsAsRead`

## No command of its own: `buttons` and `press` reach them

- Staff advice (T170): `EventFmxAcknowledgeAdvice`, `EventFmxAcknowledgeAllAdvice`, `EventFmxActionSuggestion`,
  `EventFmxApplyAllAdviceSingleCategory`
- Scouting centre options: `EventFmxScoutingCentreSelectAll`, `EventFmxScoutingCentreClearAll`,
  `EventFmxScoutingCentreOptionsEditConfirm`, `EventFmxScoutingCentreOptionsEditCancel`,
  `EventFmxPlayerInterestLevelSelected`, `EventFmxPlayerInterestTypeAndLevelChanged`
- Team report priorities (T21): `EventFmxCancelScoutingTeam`, `EventFmxPrioritiseScoutingTeam`
- Find similar: `EventFmxFilterConfirmFindSimilarPerson`, `EventFmxFilterCancelFindSimilarPerson`
- A past match's replay: `EventFmxGoToPastMatch`
- Tactic: `EventDeleteAddOn`, `RevertPlayerInstructions`
- Set pieces: `EventFmxSetPieceAskToAssignCategory`, `EventFmxSetPieceAskToAssignTakers`,
  `EventFmxSetPieceCategoryPriorityMove`, `EventFmxSetPieceTakerBothSides`, `EventFmxAddSetPieceBothFeet`
- Planner: `EventFmxSquadPlannerCopy`
- Opposition advice, one at a time: `EventFmxOppositionInstructionsNext`, `EventFmxOppositionInstructionsPrevious`
- Youth team's settings: `EventFmxConfirmTeamSettings`, `EventFmxSetTrainingIntensity`, `TeamTrainingChange`
- Responsibilities: `EventFmxRedo`
- Shortlist page: `GameplayOption`
