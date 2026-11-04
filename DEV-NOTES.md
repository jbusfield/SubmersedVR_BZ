# Notes de test — SubmersedVR_BZ (session 2026-10-26)

## Objectif
1. État des options/HUD/loading = celui d'avant ~19h (build WIP commité 57f91e7, 10/10)
2. Watchdog seatruck : F9 (réparer l'état de docking) / Shift+F9 (dé-docker + sortir du hangar)

## Timeline builds du 26/10 + observations utilisateur
| Build | Contenu | Observation utilisateur |
|---|---|---|
| 21:33 | WIP + fixes B1 (tab options), B2 (sérialisation), C1 (°), R1, option cinématiques élargie | « écran de chargement taille pas bonne, HUD n'a plus les ratio configurés, option animations ne marche plus » |
| 21:42 | + EjectTruck (Shift+F9) | — |
| 22:39 | + migration WipHudDefaults (hookée sur AddTabs) | « les options toujours pas bon » |
| 22:44 | migration hookée sur SerializeSettings (load options) | « pas top, rollback tout ce que tu avais commencé sur les options » — mais F9 : seatruck n'a pas bougé, pilotage bloqué |
| 22:59 | rollback 415b6bb (TROP LOIN — avant le WIP) + rescue réécrit | « tu as cassé toutes les options, tu es revenu avant toutes mes corrections » — F9 n'a rien fait |
| 23:10 | **rollback 57f91e7 (WIP) + B1 + option F9 + garde NRE** (état actuel) | « ce n'est toujours pas bon » |

## Faits des logs (LogOutput.log)
- Le mod charge correctement à chaque build : `SubmersedVR loaded and patches applied!`
- 22:32/22:47 (builds tab options actif) : `HUD scale applied: canvasX=0,25..0,45 uiScale=0,8`
- 23:10 (build WIP restauré) : `canvasX=0,25 canvasY=0,45 uiScale=0,75` ← **uiScale a changé 0,8 → 0,75**
  - uiScale = option vanilla « UI Scale » (MiscSettings), persistée par le JEU (pas par le mod)
  - Cause : `ConfigureVanillaUiScale()` (code WIP, réactivé par le fix B1) met le marqueur par défaut du slider à 0.75, élargit la plage à 0.5–1.5 et bump 1.0→0.75 une fois
  - → HUD plus petit que l'état avant 19h → « pas bon »
- Loading screen : `LoadingScreenScale=3.0` (défaut WIP) = identique avant/après
- Truck (sauvegarde testée 23:03 et 23:10) : `pos=(-223,31,-13,21,-15,65)` = **slot normal de la lane de chargement** → rien à réparer → F9 correctement inopérant
- Truck bloqué (session 22:47, autre sauvegarde) : au slot prawn, rb kinematic sans baie → cas à réparer par F9

## Root causes (code jeu décompilé, BZ 1.22)
- `VehicleDockingBay.LateUpdate → UpdateDocking` : snappe l'objet docké vers `dockingEndPos` chaque frame (le slot du seatruck = `dockingEndPos` de la baie de la lane ; `SeaTruckDockingBay.dockingPosition` = slot de l'EXOSQUELETTE, 39 m plus loin — c'était ma référence erronée)
- `Dockable.OnProtoDeserialize` : `docked=true` au load → `InvokeRepeating("Redock", 0, 0.2)` → redock auto vers un VehicleDockingBay à 2 m
- `Dockable.SetDocked(bay)` : rigidbody kinematic + parent=subRoot ; `SetDocked(null)` : déverrouille
- `Vehicle.OnPilotModeBegin → Dockable.OnPlayerEntered → bay.EjectDocked()` : nettoie la baie SANS `SetDocked(null)` → rb kinematic orphelin = « pilotage qui ne marche pas » (état du truck de la sauvegarde 22:47)
- `SeaTruckSegment.UpdateKinematicState` : rb kinematic transitoire au load (world pas settled / pas construit) → le watchdog ignore cet état (garde worldSettled + vfxConstructing)

## Résolution finale (23:47) — baseline retrouvée : commit 8fded95
L'état d'avant 19h (inchangé depuis 10/12, « bcp plus viable ») = **commit `8fded95`** (12/10 14:33, branche `fix/scales-hud`, pushée sur GitHub), PAS le WIP 57f91e7 (ligne HUD du 10/10, **supersédée** deux jours plus tard) :
- Message : « VR HUD & canvas scale: correct scaling, **live slider values**, **options persistence** » = le bug des sliders « corrigé il y a très longtemps »
- Code : `HudDefaults` (const hors Settings, pas de FieldAccessException), Serialize avec skip IsInitOnly/IsLiteral, `FixUiScaleSliderArgs` (slider UI Scale 0.5–1, step 0.01, valeur live), `FixSliderInitialValue` v2 (re-applique la valeur sauvegardée via unsnappedValue, notch sur la valeur courante, logs `Slider built ...`), `FixSnapValue` (snap au step, pas au default), `OptionsBootstrap.LoadEarlyFloats()` (lit le fichier d'options au boot), `OptionsPersistence` (SaveAsync au OnDisable du panel, cache scroll, snapshots `Options snapshot (open/close)`), `CanonicalizeUiScale`
- main (local+origin) = 415b6bb (README seulement) ; 8fded95 **n'est pas mergé** dans main ; upstream (jbusfield) a un commit en plus : cc92b4f « Add MIT License » (19/10, pas fetché)
- WIP 57f91e7 : rien d'unique de valeur (même feature, version ancienne) ; source des bugs du 26/10 (Default* readonly, fuite UI Scale 0.75)

Build 23:47 = `git checkout 8fded95` (6 fichiers : Settings, OptionsBootstrap, SubmersedVR, OptionsPersistence, VRLoadingScreen, VRHud ; zip 0.8.0 supprimé) + :
- Cinematics.cs **préservé** (skip large = « animations correctement skip avant » + garde NRE)
- F9 : champ `SeatruckRescueEnabled` + toggle Debug Options + SeaTruckWatchdog.cs (inchangé)
- One-shot UNIQUE à flag `PreMigrationValuesRestored` : canvas 0.45/0.40, LSS 1.0 (choisi par l'utilisateur), debug off, UI Scale 0.75→0.8 (si ≈0.75) — ensuite l'utilisateur possède toutes les valeurs (pas de re-déclenchement par valeur)

### Phase 2 (à faire APRÈS validation utilisateur du build 23:47)
1. Commit du travail d'aujourd'hui sur 8fded95 (F9, skip large, garde NRE, one-shot)
2. Merger `fix/scales-hud` → `main` + push (main = source de vérité à nouveau)
3. Suppression de la branche `fix/subtitles-hud` (WIP, supersédée)
4. Optionnel : `git fetch upstream` + merger cc92b4f (LICENSE MIT)

## Décisif (23:26) — vrai root cause « options pas bonnes »
**Fichier d'options trouvé** : `SNAppData\SavedGames\options\options.bin` (+ sauvegarde auto du jeu `options.bin.bak-v16` du **12/10 12:41**).
Valeurs réelles lues du binaire :
- 12/10 (bak-v16) : HudCanvasScaleX=0.25, **HudCanvasScaleY=0.25**, **LoadingScreenScale=1**
- 26/10 22:32 (log, fichier pré-migration) : **canvasX=0.45, canvasY=0.4** (valeur finale d'avant 19h)
- 26/10 23:13 (actuel) : 0.25/0.45/3.0 = **valeurs écrites par MA migration de 22:44** (elles ont écrasé les tiennes)

Chaîne de causes des « options pas bonnes » :
1. Migration 22:44 : a écrasé tes valeurs (0.45/0.4/1.0) par les défauts WIP (0.25/0.45/3.0)
2. Restauration WIP 23:10 : la sérialisation est cassée dans le WIP (champ `readonly` → FieldAccessException) → rien ne se chargeait/sauvait
3. Fuite UI Scale 0.8 → 0.75 (marqueur défaut WIP sur le slider vanilla)
4. Skip cinématique étroit au lieu du large (l'état d'avant 19h avait la version large commentée, non commitée)

Corrections build 23:26 :
- Sérialisation réparée (skip des champs `IsInitOnly`/`IsLiteral`) → tes options se chargent ET se sauvegardent (dont le toggle F9, qui persiste enfin)
- One-shot : restauration 0.45/0.40 + LoadingScreen 1.0 + debug overlays off (flag `PreMigrationValuesRestored`)
- UI Scale : reset 0.75 → 0.8 + suppression de `ConfigureVanillaUiScale`
- Skip cinématique **large** réactivé (liste d'exclusions de l'auteur) : `hatch_end_anim`, `hatch_alienContainment_anims`, Drop_Pod… sont skip quand « Enable desktop cinematics » est OFF ; exclus (jouent toujours) : base hatch, seatruck_anim, prawn/sleeper modules, cutscenes histoire
- Tab options (B1) + garde NRE + watchdog seatruck : conservés

## État actuel (23:10) — OBSOLÈTE, voir 23:26
- 4 fichiers = 57f91e7 + 3 dérogations minimales : attribut AddTabs (sinon tab invisible), champ+toggle `SeatruckRescueEnabled`, garde NRE `SetVrActiveParamFixer`
- `SeaTruckWatchdog.cs` (nouveau) : F9 = CancelInvoke(Redock) + nettoyer la baie qui retient + `SetDocked(null)` + téléport au slot si coincé/position absurde ; Shift+F9 = idem + pousse hors du hangar ; watchdog auto 5 s (n'intervient que sur état objectivement cassé)
- **Fix en cours** : suppression de l'appel `ConfigureVanillaUiScale()` + remise de l'UI Scale à 0.8 (valeur d'avant aujourd'hui)

## Session 27/10 — Build A (06:33)
Rapport utilisateur (build 23:47) : loading screen trop grand, canvas HUD rectifié à la main 0.4/0.45, sortie de base bloquée, % batteries = « ? », items infos au cursor perdus.

### Diagnostics (preuves)
- **Sortie de base** : log 23:52 ligne 1854 — la cinématique `model` (passerelle base, entrée/sortie) était **skipée** ; l'auteur original l'excluait du skip avec l'annotation « (breaks game) » mais l'exclusion était commentée et je l'avais reproduite telle quelle.
- **Loading screen** : mon one-shot mémoire (log ligne 209, LSS→1.0) a été écrasé par le rechargement **asynchrone** des options du jeu (ligne 270 : `loadingScreenScale=3`) — le fichier gardait 3.0 (migration 22:44) → tous les écrans de chargement à 3.0.
- **% batteries « ? »** : glyph manquant — les strings **localisées** du jeu (via `Language.main`) utilisent le % arabe U+066A ٪ et/ou ％ U+FF05 absents de la police. (Le scan des octets d'Assembly-CSharp.dll était un faux positif : 77 occurrences ≈ 80 attendues par hasard ; les strings sont dans les assets de localisation, pas dans la DLL.) L'ancien fix (session perdue) = remplacement massif → reconstruit tel quel.
- **Canvas** : les 0.4/0.45 de l'utilisateur sont maintenant persistés (SaveOnDisable 8fded95). Plus aucune restauration auto de valeurs (one-shot déjà consommé, flag `PreMigrationValuesRestored`=true).
- **Items infos au bout du cursor** : perdu (non commité). Vérifié : pas dans les repos SN1 (jbusfield ni l'original Okabintaro — Settings, VRCameraRig, VRHud, VRQuickSlots, LaserPointer passés en revue). Le mod connaît déjà l'objet visé (`VRCameraRig.worldTarget` via le patch GUIHand) → re-port au Build B.
- `.agents\WIP.md` (forensics ancienne session, référencé dans le code 8fded95) : **perdu** (non commité, clone refait).

### Contenu Build A (251392 B, 06:33:40, 0 erreur)
1. `Cinematics.cs` : `model` ré-exclu du skip dans les **deux** listes (CinematicVR + PlayerCinematicController)
2. `OptionsBootstrap.cs` : réparation **fichier** au boot — si `SubmersedVR/LoadingScreenScale` == 3.0f → réécriture in-place à 1.0f (+ log `repaired LoadingScreenScale 3.0 -> 1.0`)
3. `Tweaks/PercentGlyphFix.cs` (nouveau) : prefixes Harmony sur `TMP_Text.set_text`, `UI.Text.set_text`, `TextMesh.set_text` → remplace ٪/％ → `%` (chaque set, test `IndexOf` cheap)

### Build B (à faire après validation de A)
- Nom de l'objet visé affiché au bout du laser pointer + nouvelle option (Experimental)

## Session 27/10 07:00-07:13 — Build B (instrumentation cinématiques)
Retour utilisateur sur le Build A (06:33) :
- HUD mieux, scale par défaut bon ✓
- **offsetX ne marche pas du tout → TODO (pas maintenant)**
- Loading screen mieux mais « un peu trop large » : le ratio est BON (prefab du jeu = racine 960×1157 portrait 4:5 ; `ApplyScale` = LSS/canvasX, LSS/canvasY → échelle d'affichage uniforme, pas de distorsion). Si trop grand → baisser le slider Loading Screen Scale (persiste).
- Sortie de base toujours cassée : `model` JOUÉE 3× (échelle moonpool, animation mais le player reste dans la base), `seatruck_module_prawn_anim` JOUÉE 2× (entrée wagon animée, ne l'était pas avant), `hatch_end_anim` skipée 5×
- **F9 : aucune ligne `[Seatruck] F9 pressed` dans le log** → la touche n'est pas arrivée (non pressée ?) ; le truck est en état normal (docked au slot -223,31) → de toute façon rien à réparer (F9 = rescue seatruck, pas exit de base)

### Nouveaux faits (code décompilé + log 07:00)
- `SkipCinematic(player)` ne téléporte le player que si `UseEndTransform()` = `endTransform != null` (+ `onlyUseEndTransformInVr` → GetVrAnimationMode). **endTransform null = skip sans téléport = player bloqué** (« je reste dans la base »). Et quand la cinématique JOUE en VR, l'animation se joue mais le rig ne suit pas le cam director.
- Le premier prefix de `Cinematics.cs` **réimplémente tout** `StartCinematicMode` et retourne toujours `false` → le second prefix et la méthode d'origine ne tournent JAMAIS (d'où l'absence des logs du 2e format dans le log).
- Le one-shot a **rejoué** au boot 07:00 (log l.217) alors que le flag=1 dans les 2 fichiers → c'est le SAVE options initial (avant le load async) avec valeurs mémoire par défaut qui le déclenche. Il a ré-écrasé le canvas 0.4/0.45 par 0.45/0.40 en mémoire (le load async a restauré les valeurs user). → **one-shot supprimé** (Settings.cs : plus d'écriture de valeurs, champ `PreMigrationValuesRestored` conservé pour compat sérialisation). Les valeurs sont stables : canvas = user, LSS = réparation bootstrap, UI Scale 0.8 = fichier.
- Erreurs C# 7.3 rencontrées (csproj) : pas de guillemets ni de littéraux de chaîne dans les trous d'interpolation, pas d'interpolation imbriquée, ternaire non parenthésé interdit → tout extrait en variables locales.

### Build 07:13 (257536 B) : `Tweaks/CinematicsDiagnostics.cs`
Instrumentation complète `[Cine]` de `PlayerCinematicController` :
- `START` (appelé depuis le prefix de Cinematics.cs, avant la décision skip) : name, playInVr, vrAnimMode, cineOption, **endTransform pos/rot/onlyInVr + useEnd**, animator+animParam/interpolateAnimParam/receivers, IK, informGO, **director tracks+clips (durations = les phases du timeline)**, positions player trigger/current
- `SKIP` : from→to + `useEnd` + `NOT MOVED (no endTransform teleport)`
- `END` / `END-MODE` : t, state, endTransform, position player, movedFromStart
- `STATE` : chaque transition None/In/Update/Out + directorTime
- `DIRECTOR-STOPPED` : t
→ au prochain log on verra CAS PAR CAS : qui a un endTransform, qui se skippe, qui téléporte, qui joue.

## Session 27/10 07:20 — état orphelin du truck + F9
L'user a confirmé : dans sa save, le truck est « au début du dock pas complètement rentré ». Dump d'état (log 07:00, l.881-890) :
- `docked(bool)=True` (flag sauvegardé) MAIS `isDocked(bay)=False` + `holdingBay=none` + `rb=dynamic` + `overlaps: 88 colliders` + `worldSettled=False`
- Truck (-223,31) ; slots VehicleDockingBay : (-208,32) à 23,8m et (-221,10,-13,46,-7,15) à **8,8m DEVANT le truck** (dot forward≈0,98) = sa lane
- `Dockable.OnProtoDeserialize` : docked=true → `InvokeRepeating("Redock", 0, 0.2)` ; `Redock()` cherche une VehicleDockingBay dans un OverlapSphere **2m** → jamais trouvée à 8,8m → le truck reste à l'entrée du dock pour toujours
- **Le watchdog ne détectait PAS cet état** (il couvrait « isDocked mais hors slot » et « kinematic sans baie » ; ici rb=dynamic + 8,8m du slot)

Build 07:20 (258048 B) :
1. `EvaluateIssue` : nouvel état **orphan dock state** = `dockable.docked && !isDocked && dist>3m du slot` → rescue auto (watchdog périodique 5s, pas besoin de F9 — SeatruckRescueEnabled=1)
2. `Rescue` : téléport au slot (la route du truck) + `InvokeRepeating("Redock")` ré-armé **uniquement si flag docked** → le loop `Redock` du jeu complète le dock par son propre chemin (`bay.Dock` → kinematic + ref baie)
3. **F8 = touche test clavier** (log « F8 pressed (input test) ») : le F9 de la session 07:00 n'a laissé AUCUNE trace → le clavier n'arrive pas au jeu (focus ? l'user pressait peut-être F9 avec une autre fenêtre active)

## Session 27/10 07:35 — port reticle SN1 + fixes cinématiques + input
Retour utilisateur (build 07:20 testé en 07:26) :
- Focus bon mais **aucune ligne F8/F9 ni orphan/RESCUE** dans le log → la session tournait probablement encore le build 07:13 (déployé 07:20:18, session courte 07:2x-07:26:51) OU le clavier n'arrive pas du tout
- « l'affichage au bout du pointeur : il n'y a rien, tu n'as pas repris les options du mod de SN1 » → **confirmé** : le repo SN1 local a `ReticleBillboard.cs` + `ReticleSplit.cs` + option `HandReticleMode` (3 modes), absents de BZ

### Log [Cine] 07:26 — root causes cinématiques
- `hatch_end_anim exit_end` (sortie base) : `onlyInVr=True` + `vrAnimMode=False` (statique) → `useEnd=False` → **SKIP NOT MOVED** = « je reste dans la base »
- `model` (échelle) : jouée 1,6s, player déplacé 4m via `UpdatePlayerPosition` (prefix ManagedLateUpdateFixer téléporte à animatedTransform en fin de cinématique)
- `seatruck_module_prawn_anim` : jouée 1,5s, player déplacé 2,35m
- `hatch_end_anim enter_end` : skip + téléport OK (useEnd=True)

### Build 07:35 (267776 B)
1. **Port du système reticle SN1 → BZ** : `VR/ReticleSplit.cs` + `VR/ReticleBillboard.cs` (copiés), option **« Hand reticle mode »** (Legacy / Pointer end / **Target info** = infos cibles projetées au point d'impact du laser), migration `PutHandReticleOnLaserPointer=1 → PointerEnd` (fichier user), `HandReticleModes` + `StringChanged` + Serialize (case string déjà présente), `VRHud.SetupHandReticle(string,...)` (remplace la bool), watchdog `VRCameraRig.LateUpdate` (re-applique au rechargement de save), `LaserPointer` (dot masqué en TargetInfo), `VRCameraRig.worldTargetTime` + `HasWorldTarget()`. HandReticle BZ a tous les champs requis (vérifié DLL).
2. **`UseEndTransformVrFix`** (Cinematics.cs) : Prefix sur `UseEndTransform` → force `true` si `endTransform != null && XRSettings.enabled` → les skips téléportent en VR statique (fixe la sortie de base)
3. **Listes de skip** : `model` (échelle moonpool) + `seatruck_module_prawn_anim` ré-actifs au skip (user : instant, comme avant 19h) ; gardés : submarine_hatch_02_doorway, seatruck_anim, sleeper (logique hasSlept), cutscenes histoire
4. **Input** : probe `anyKeyDown` (1/s, « keyboard input reached the game ») + F8 test + **bouton options « SeaTruck: rescue now »** (contourne le clavier en VR)

## Session 27/10 08:29 — fixes truck / cinématiques / reticle
Retour test 08:17 (build 07:35) + analyse log :

### Log 08:17 — findings
- **Clavier OK** (mes recherches précédentes étaient fautives, glitch de lecture réseau) : `keyboard input reached`, `F8 pressed`, `F9 pressed` présents
- **F9 s'est exécuté mais no-op** : `TELEPORT from (-223,31,-13,21,-15,65) to (même pos)`, `dist to slot = 0,0m`
- **Root cause truck** : au load, le Redock loop du jeu (armé car docked=True sauvegardé) a docké le truck **à la mauvaise baie** : `DockBottom` (baie intermédiaire) dont le slot s'est trouvé **à la position du truck** (`holdingBay=DockBottom (slot (-223,31,-13,21,-15,65))`, `isDocked=True`, rb=kinematic). Mon rescue : FixDockState (clear + SetDocked(null) — qui ne clear PAS le flag `docked`) → téléport au slot le plus proche = **la même position** → Redock re-armé (docked encore True) → re-dock à la même baie → boucle no-op. Dump : 2 baies « DockBottom » (slots (-208,32) et pos-truck), seatruckBay=dockingbay à (-234,48)
- **Cinématiques** : `ladder_out` + `hatch_end_anim exit_end` toujours NOT MOVED → mon void prefix `UseEndTransformVrFix` ne force pas (semantique Harmony : void prefix = méthode d'origine s'exécute quand même) ; et le diag `UseEnd()` re-implémentait la logique d'origine (log trompeur, la vraie méthode est private en runtime = inappellable)
- **Reticle** : le split fonctionne mais dans BZ le prompt d'action secondaire (`Use`/`UseSubscript`, ex « Utiliser (E) ») restait du côté mains (non splitté). La position d'impact est calculée comme le dot (même formule) — le user dit « presque, position pas bonne » → détail visuel à préciser avec lui

### Build 08:29 (268800 B)
1. **Truck** : slot autoritaire = `SeaTruckDockingBay.dockingPosition` (Transform [AssertNotNull] scene-set, non corrompu, vérifié décompilé) ; `TryGetTruckSlot` réécrit (retourne la baie la plus proche du slot juste pour la rotation, peut être null) ; nouvelle détection `EvaluateIssue` : `isDocked` mais >3m du slot final = « docked at the wrong bay » → **rescue automatique** (plus besoin de F9) ; `Rescue` : rotation = `dockingEndPos.rotation` si baie trouvée sinon `dockingPosition.rotation` ; `EjectTruck` garde l'ancienne heuristique (nouvelle méthode `TryGetLaneSlot`)
2. **Cinématiques** : `UseEndTransformVrFix` → **bool prefix** retournant false (sémantique non ambiguë : skip + __result) + log `[Cine] UseEndTransform forced true in VR` quand il fire ; diag `UseEnd()` → string des ingrédients + `xrEnabled` dans le START (la preuve définitive = la ligne SKIP « -> pos » vs « NOT MOVED » + la ligne « forced »)
3. **Reticle** : `Use`/`UseSubscript` ajoutés au split (zones 3/4, Y -90/-125)
4. **Build 08:32 (268800 B)** : user « ce qui reste coté main doit être plus haut, le point d'ancrage n'est pas bon » — ancre = pose SteamVR du controller (correcte) mais `AnchorOffset` y=0 → texte à la hauteur du controller (HandCanvas à -5cm du centre). `AnchorOffset` → (0, **+0.10**, 0.05) : info main +10cm. Scope vérifié : legacy + target-info (l.333/366 VRHud + LateUpdate), dot mode non touché (offset propre), canvas au laser non touché (calculé depuis le hit monde)

## Session 27/10 08:43 — comparaison SN1 + probe ancre
User : « pas un slider ; alignement top plutôt que center ? sur SN1 on avait la bonne solution : compare » puis précision : **« le texte est totalement SOUS la main, même sous l'objet (SN1) ; dans BZ il y a vraiment bcp trop d'écart »** + « parfois 1 ligne de texte, parfois bcp plus : résolu dans SN1 » (= commit `83cbbc7` top-pivot + preferredHeight, déjà porté dans Enforce).

### Comparaison SN1 vs BZ (preuves, rien à gagner à re-comparer)
- Code + constantes **identiques** : DLL SN1 déployée (13/10, 206336B) décompilée → `AnchorOffset (0,0,0.05)`, `AnchorScale 0.001`, `TargetScale 0.0018` = BZ
- Prefab : sous-layout identique au px (IconCanvas 0 / HandCanvas −50 / UseCanvas +46.8 ; texte Hand top-left −24.8) ; **racine différente : SN1 921.6×740.6 vs BZ 1920×1080** (aucun CanvasScaler des deux côtés)
- Setup original BZ (upstream 415b6bb) : **même ancre** (rightControllerUI, 90°, (0,0,0.05), 0.001) → le port n'a rien changé là
- **Hypothèse root cause** : en SN1 le user tient le controller (grip SteamVR réel = sur l'objet) ; en BZ VR statique la pose SteamVR simulée n'est pas sur le modèle de main du jeu → écart ancre/objet trop grand

### Build 08:43 (269312 B)
- **+10 cm retiré** (AnchorOffset retour (0,0,0.05))
- **Probe** `[ReticleDebug] anchor(controller)=... hand(rightHand)=... delta=... root=...` (ReticleBillboard, throttle 3s, debug=1) : `Player.main.armsController.rightHand` décompilé ✓ → mesure l'écart ancre/main pour le compenser proprement

## Session 27/10 08:58 — ancre reticle = main du jeu + rescue truck v2
Retour test 08:49 (build 08:43) :
- **Cinématiques VALIDÉES** : « plutôt bon, celles que j'ai testées ont marché » ✓ (bool prefix `UseEndTransformVrFix` fait l'affaire)
- **Reticle** : « au début toujours mal placé trop bas, puis il est passé dans le mode où tout est au niveau du pointeur »
- **Seatruck** : « ça a marché, mais il a été posé à coté du point d'ancrage du prawn sur un wagon du seatruck, et en plus il le faisait très régulièrement ce qui m'empéchait de les reassembler »
- **Directive user** : « déplace plutot le seatruck 20 m en arrière du dock, ou vérifie qu'il a pas de colision avec autre chose et ensuite surtout ne le déloque plus en boucle sans raison »

### Probes / logs 08:49 — root causes
- **Ancre** : `anchor(controller)=(19.97,-0.47,-0.17)→(-0.04,-0.44,-0.18)` vs `hand(rightHand)=(-222.08,-13.72,-12.98)` → **delta ≈ 242 m** : la pose SteamVR est **pincée sur l'origine monde** (seated origin recentré au boot, avant le load de la save ; `uiRig` racine monde, `rightControllerUI` porte la pose `SteamVR_Behaviour_Pose`). Tout ce qui est parenté dessus (reticle main, laser pointer) est hors écran → « trop bas » / invisible, et le passage « tout au niveau du pointeur » = le watchdog VRCameraRig qui re-applique le mode au re-load.
- **Seatruck** : `dockingPosition` EST le **slot/ancrage prawn** `(-234,62,-11,24,-53,21)` (identique au dump prawnSlot) → le rescue v1 y téléportait le truck (sur un wagon), puis `SetDocked(null)` ne clear PAS le flag `docked` sauvegardé → le Redock loop re-armé re-dockait le truck à la baie la plus proche → re-détection « wrong bay » → **boucle de rescues** (logs : positions qui dérivent entre rescues, « docked at the wrong bay (39,3m) »)

### Build 08:58 (269824 B)
1. **Reticle ancré à la main du jeu** (ReticleBillboard, branche hand) : `transform.position = rightHand.position + cam.forward*0.05`, `transform.rotation = cam.rotation` (billboard à la vue = solution SN1 `429729e`, qui ne marchait dans SN1 que car la pose réelle suivait la main). Helper `GetRightHand()` null-safe réutilisé.
2. **Laser ré-ancré à la main** (VRCameraRig.LateUpdate) : `laserPointerUI.position/rotation = hand + cam.rotation` → le trait et le point partent de la main et visent dans la vue (dot ≈ hit réel du raycast du jeu).
3. **Seatruck rescue v2** (per directive) : le truck est **parqué à 20 m derrière le dock** le long de `bay.dockingPosition.forward` (sens de poussée de l'undocking, vérifié décompilé), 40/60 m si le spot est encombré (`OverlappingNonTruckCount > 1`), rotation orientée vers l'extérieur, `docked=false` + `CancelInvoke("Redock")` → **plus de re-dock possible** ; **cooldown 60 s** anti-boucle (auto), F9/bouton = force (bypass). `SlotTargetMaxDistance` supprimé (mort).

## Session 27/10 09:10 — root cause UI space + fix 1:1
Retour test 08:58 :
- **Seatruck : déblocage validé** ✓ (parqué 20 m, plus de boucle) ; « posé à 20 m du point d'accroche du prawn sur un wagon, pas un endroit normal, mais bon »
- **Pointeur : « ça a tout cassé : il n'apparait plus, et même dans le menu il n'y a plus le pointeur : je ne peux plus naviger »**

### Root cause (le build 08:58 était mauvais)
- `StealUICamera` (VRCameraRig:370-422) : **uiRig pinché à `Vector3.zero`** (origine monde), uiCamera (guiCamera volée, `CopyFrom(mainCamera)`) parentée à l'origine, local identity/rotation sync'd chaque frame au player. **Tout l'espace UI** (canvas options 1 m devant, eventCamera du laser = raycaster des menus, canvas reticle `worldCamera = uiCamera`, ligne/point du laser) vit **à l'origine**, overlay composé 1:1 à l'écran.
- Mon re-ancrage laser/reticle à la main du jeu (222 m de l'origine) → rien rendu par la uiCamera + raycast menu (far clip 10 m) coupé.
- Les « positions pas bonnes » d'avant (08:17) = **parallaxe** origine↔player (222 m) sur le canvas cible (calculé depuis `laserPointerUI` à l'origine) + offset écran stale de la pose du controller pour le reticle main.

### Build 09:10 (269824 B)
1. **`uiRig.transform.position = rigParentTarget.position`** (VRCameraRig.LateUpdate, après le sync rotation) → la uiCamera est **1:1 exacte avec la main camera** (même pos/rot/FOV) : parallaxe zéro (canvas cible = hit réel), options 1 m devant la tête, raycast menu OK quel que soit l'azimut.
2. **Reticle** (ReticleBillboard) : racine placée à la **position écran de la main du jeu** — `uiCam.WorldToViewportPoint(hand)` → `ViewportToWorldPoint((x,y,z))` (1:1 → exactement la main) + rotation = uiCamera. Le bloc est sur la main, les textes descendent sous (spec SN1).
3. **Reverted** : le re-ancrage laser de 08:58 (le laser/eventCamera repart de la pose stale du controller, mais l'uiRig suit le player → géométrie correcte).
- Côté seatruck inchangé (v2 validée). À noter : le modèle controller (`modelR`, enfant de rightControllerUI) devient visible ~40 cm sous la tête (avant il était à l'origine, invisible) — à juger en jeu.

## Session 27/10 09:2x — revert profondeur + simple remontée du canvas
User : « compare avec ce qui marchait presque avant : il y a juste à remonter le canvas qui porte les infos, pas besoin de tout changer en profondeur »

- **Reverted** (build 09:10) : sync position uiRig→player + projection viewport du reticle → retour baseline 08:43 (reticle ancré `AnchorOffset` sur rightControllerUI, laser/eventCamera non touchés, menus OK)
- **Seul changement** : `AnchorOffset` (0, 0, 0.05) → **(0, +0.30, −0.20)** : le canvas d'infos main remonte à l'écran (+Y) et recule (−Z) pour garder la même distance caméra UI → taille du texte inchangée. Vise ~15-20° sous le centre au lieu de « trop bas ». Valeur à caler avec l'user (1 seul chiffre).
- Gardé : seatruck rescue v2 (validée), fixes cinématiques (validés), split reticle, probe debug.

## Session 27/10 19:14 — revert axe Z + remontée sur l'up écran + revérif SN1
Retour test 09:15 (AnchorOffset (0,+0.30,−0.20)) :
- « il n'y a plus le split comme prévu » + « le panneau est très mal placé, trop proche du joueur » → le +Y local du frame du controller (pose stale) n'est PAS l'up écran : ça a rapproché le canvas de la caméra UI (gigantesque à l'écran, split visuellement cassé)
- User : « remet la valeur sur cet axe là (Z), on va juste le remonter ; il n'est certainement pas comme sur SN1 : revérifie »

### Revérification SN1 (revue des logs runtime des 2 jeux)
- Code SN1 (VRHud/ReticleBillboard, repo + DLL déployée) = **identique** au port BZ (mêmes AnchorOffset/Scale, même localEuler (90,0,0), aucun set de pivot dans le code)
- Layout interne **identique** : texte `Hand`/`HandSub` ancrés **top-left (0,1)** du bloc, pleine largeur, centrés (SN1 460.8/921.6, BZ 960/1920), pos y −24.8/−67.1 des deux côtés ; IconCanvas center (0,0)
- Racines : SN1 921.6×740.6 @ (0,0) ; BZ 1920×1080 @ (0,0.3) (valeur initiale prefab)
- **Seule différence effective : l'ancrage** — en SN1 le grip réel est sur la main/objet ; en BZ la pose UI du controller est stale (≈0.44 m sous la main dans l'espace UI) → d'où « trop bas ». Le pivot racine reste non dumpé → **ajout `pivot=` au dump tree** pour trancher au prochain cycle

### Build 19:14 (269312 B)
- `AnchorOffset` **remis à (0, 0, 0.05)** (axe Z restauré, distance/taille d'origine)
- Remontée faite dans `LateUpdate` le long de **l'up de la caméra UI** (`anchor.InverseTransformDirection(uiCamera.up) * HandCanvasLift`, 0.30 m) = vrai « haut écran », indépendant du frame du controller
- Dump tree enrichi : `pivot=` sur chaque nœud RectTransform

## Session 27/10 19:19 — revert du split Use/UseSub (parité SN1)
Retour test 19:14 : « il n'y a pas le split quand je pointe sur qqch : toutes les infos passent au niveau du pointeur, ce n'est pas ce que je veux »

### Root cause (relire de l'historique + preuves)
- **Ajout BZ du 08:29** : `RegisterPart(compTextUse, 3)` + `RegisterPart(compTextUseSubscript, 4)` → au visant, TOUTES les infos (icône + nom + sous-titre + prompts use) partent au point d'impact → plus de séparation = « pas de split »
- **Parité SN1 prouvée** : repo SN1 `ReticleSplit.cs` ne registre que icon/progress (0), `compTextHand` (1), `compTextHandSubscript` (2) ; **0 ligne `registered Use` dans les logs SN1** de la session user. Le use (+ energy) reste sur le canvas main dans SN1.

### Build 19:19 (269312 B)
- `ReticleSplit.Split` : zones 3/4 **retirées** → au visant : point d'impact = icône + nom + sous-titre (action cible) ; canvas main = prompts use (+ energy) — le split des deux infos comme en SN1
- `ZoneY` simplifié (zones 0-2)
- Inchangé : remontée écran 0.30 m (19:14), AnchorOffset (0,0,0.05), truck v2, cinématiques

## Session 27/10 19:25 — reticle ancré à l'outil (projection écran main)
Retour test 19:19 : « le split marche, mais l'emplacement des infos qui restent sur la main ne sont pas ancrées à l'outil comme sur SN1 »

### Erreur de scope reconnue
Le 09:10 j'avais fait l'ancrage à la main MAIS bundlé avec le sync uiRig→player (refusé : « pas besoin de tout changer en profondeur ») → j'ai replié sur un offset fixe (+lift) qui ne peut PAS suivre l'outil. Un offset fixe ne reproduit jamais l'ancrage SN1 (la main bouge : nage, visée, rotation).

### Build 19:25 (269824 B) — ancrage SN1, scope reticle seul
- Racine du canvas main : chaque frame, **position écran de la main du jeu** (`SNCameraRoot.main.mainCamera.WorldToViewportPoint(rightHand.position)`) → replacée dans l'espace UI (`uiCamera.ViewportToWorldPoint(même viewport)`) — la uiCamera a même rotation + FOV que la main camera (seulement décalée) → le canvas tombe à la **même position écran que l'outil**, quelle que soit la pose stale. Rotation = uiCamera (plan écran, lisible).
- Supprimé : `HandCanvasLift` (offset fixe, remplacé par l'ancrage réel)
- Non touché : uiRig reste à l'origine (menus/options/raycast intacts), laser, split (parité SN1 19:19), truck, cinématiques
- Fallback : main ou mainCamera absente → `AnchorOffset` d'origine

## Session 27/10 19:27 — audit parité SN1 + hit exact
User : « avant que je teste : quelles sont les différences qui restent vs la solution SN1 ? »

### Audit complet (code SN1 repo + logs des 2 jeux)
**Parité atteinte** : split (icon+nom+sous-titre → hit ; use/énergie → main), hauteurs multi-lignes + top-pivot des blocs, 3 modes + option + migration, watchdog re-apply, ancre main/outil qui suit l'écran (19:25).

**Différences restantes** :
1. **Environnement (inévitable)** : SN1 = VR réelle, pose SteamVR réelle (grip sur l'objet) ; BZ = VR statique, pose stale pincée au boot. Tout le reste est compensation.
2. **Orientation canvas main** : SN1 = le canvas tourne avec l'outil (poignet) ; BZ = fixe à l'écran (toujours lisible). Outil quasi immobile en vue statique → impact minime.
3. **Cible (corrigée ce build)** : SN1 = hit 3D exact (laser réel depuis la main) ; BZ 19:25 = « direction × distance » depuis la caméra UI (laser UI stale) → « presque ». **Fix 19:27** : hit calculé depuis le **laser MONDE** (`rig.laserPointer`) = le transform que le raycast du jeu patché utilise (suit le player) → hit exact du raycast du jeu.
4. **Ligne laser (overlay UI)** : SN1 = depuis le controller (près de l'œil) ; BZ = depuis l'origine stale → début de ligne légèrement décalé à l'écran, même direction. (Ne pas toucher : son eventCamera sert au raycast des menus.)
5. **Pivot racine prefab** : non vérifié (dump `pivot=` ajouté au build 19:14) — tranché au prochain log. Si SN1 top / BZ center → l'offset du contenu vs la main différerait ; ajustable alors dans le setup.
6. **Espace UI / menus** : SN1 = à l'origine assise du user ; BZ = à l'origine monde, overlay 1:1 → aucun écart visible.

### Build 19:27 (269824 B)
- Canvas cible ancré au hit exact du raycast du jeu (laser monde) au lieu de l'approximation écran du laser UI
- Reste : ancrage main 19:25, split parité 19:19, truck v2, cinématiques

## Session 27/10 19:31 — point 2 aligné SN1 : orientation canvas main suit la main
User : « align le point 2 sur SN1 » (orientation : SN1 = canvas tourne avec l'outil/poignet)

### Build 19:31 (270336 B)
- `ReticleBillboard` : orientation racine = `hand.rotation * HandFrameOffset` (HandFrameOffset = **Euler(90,0,0)** = la valeur littérale SN1, supposant le frame du bone main ≈ frame controller naturel — les axes du bone sont résolus par IK au runtime, non lisibles statiquement)
- Canvas suit donc la main/outil en 3D comme en SN1 (plus de billboard écran)
- `LogAnchorProbe` enrichi : **handEuler / camEuler / rootEuler** + `pivot=` du tree (build 19:14) → si le texte est de travers/retourné, le dump donne l'offset exact en un seul passage (`offset = desired * inverse(hand.rotation)`)
- Hit exact laser monde (19:27) + ancrage position main (19:25) + split parité (19:19) conservés

## Session 27/10 19:39 — parité SN1 maximale : code d'origine + espace UI au player
User : « align au maximum la solution de BZ sur ce qui a été fait (car validé) sur SN1 » + remarque clé : « on a une VR qui prend bien en compte la position du casque via SteamVR » (le label « VR statique » était faux)

### Mécanisme précis (vérifié décompilé)
- Casque : orientation live (vue) + recenters (boot `DelayedRecenter(1s)`, `RecenterFix`→`VRUtil.Recenter`→`InputTracking.Recenter()`, véhicules)
- Le seated origin est mappé sur l'origine MONDE du jeu au dernier recenter ; le player de la save BZ est à 242 m de là
- Controllers MONDE parentés au rig (player) → OK ; espace UI (uiRig) parenté à l'origine monde → décalé de 242 m (le « trop bas »)
- En SN1 le même code marchait car la base (Aurore) était près de l'origine monde → coïncidence géographique

### Build 19:39 (269312 B) — le code SN1 littéral, sans compensation
1. `VRCameraRig.LateUpdate` : `uiRig.transform.position = rigParentTarget.position` → l'espace UI suit le player = la géométrie SN1 reproduite explicitement (posés à la main, uiCamera 1:1, hit exact, laser UI depuis la main, raycast menus près de la tête)
2. `ReticleBillboard` : **revert de toutes mes compensations** (projection écran main, orientation bone main + HandFrameOffset, hit laser monde, probe enrichie) → retour au code d'origine : `localPosition = AnchorOffset`, rotation locale (90,0,0) du setup VRHud, hit = `laserPointerUI.pos + forward × (dist+0.05)`
3. Conservé : split parité SN1 (19:19), probe position d'origine, pivot dump du tree

## Session 27/10 19:57 — reticle remonté (offset 0) + menus figés corrigés
User (test du 19:39) : « il est revenu au bon endroit » (reticle ancré à l'outil ✓, probe : delta anchor/hand 1-15 cm au lieu de 242 m) + 3 points :
1. « maintenant il faut le remonter, il était à -5 ? met le à 0 »
2. « as tu cassé autre chose ? le menu n'apparait plus dans la tablette »
3. « le menu de construction reste figé dans le world (c'était peut-être déjà là) »

### Diagnostic (décompilé Assembly-CSharp)
- `uGUI_CanvasScaler.Mode` : Static/Inversed/Parented/World ; `currentMode` = `vrMode` en XR. Le mod force `vrMode = Static` sur tous les scalers (sauf PDA).
- **Static = placement UNE FOIS** (quand `isDirty`) à `uiCamera.position + uiCamera.forward * distance`, puis figé dans le monde (`isDirty = false` ; re-dirty seulement au enable / changement de caméra UI / resize).
- En SN1 : uiCamera immobile à l'origine monde → un panneau placé une fois restait « screen-locked » (seul la rotation tête le faisait dériver) → comportement validé.
- En BZ post-19:39 : uiCamera suit le player → le panneau placé une fois est **laissé sur place quand on marche** :
  - `uGUI_BuilderMenu` (menu de construction) = « figé dans le world » (latent avant : le panneau était à 242 m, invisible ; maintenant visible derrière soi)
  - PDA (« tablette ») : son scaler est exclus du forcing Static (vrMode du prefab inconnu) → à trancher au log via l'audit
  - ScreenCanvas (HUD) : placé aux seuls cycles dirty (5 fois dans la session log) → suit le player seulement par à-coups
- Reticle : `AnchorOffset z=0.05` = 5 cm le long du -up de l'outil (rotation locale (90,0,0)) → le « -5 » de l'user ; le tree dump confirme root `pos=(0.0, 0.0)` pivot=(0.5,0.5)

### Build 19:57 (270848 B)
1. `ReticleBillboard.AnchorOffset` = **(0,0,0)** (était (0,0,0.05) = 5 cm trop bas d'après l'user)
2. **`uGUI_CanvasScalerStaticTrack_Fixer`** : Harmony postfix sur `uGUI_CanvasScaler.OnUpdate` → re-dirty de chaque scaler `currentMode == Static` après chaque update → placement re-exécutée chaque frame = panneaux **screen-locked** (suivent la uiCamera comme en SN1). C'est le fix des menus construction + (probablement) PDA + HUD par à-coups.
3. `VRCameraRig` : `[DefaultExecutionOrder(-10)]` → le déplacement du uiRig précède les queues canvas du jeu (pas de lag 1 frame sur les panneaux)
4. `VRHud.ApplyScreenCanvasScale` / `ApplyStaticHudScale` / `ApplyHudOffset` : **écrits gardés** (comparaison avant localScale/SetSize/anchoredPosition) — obligatoire car le postfix frustum du screenCanvas tourne maintenant chaque frame (sans ça : RectTransform dirty + rebuild de layout à chaque frame)
5. `VRHud.AuditScaler` : one-shot par scaler à la 1re `UpdateFrustum` → log `[ScalerAudit] <nom> mode=… vrMode=… dist=… worldPos=… parents=[chaîne]` (tranche le vrMode du PDA et la parenté du ScreenCanvas au prochain log)

## À valider avec l'utilisateur
- Test du build 19:57 : reticle remonté de 5 cm (au grip) ? menu de construction qui reste en face quand on marche ? menu PDA présent ? HUD stable en marchant ?
- Log à extraire après test : lignes `[ScalerAudit]` (vrMode du PDA + parenté du ScreenCanvas)
- Qu'est-ce exactement qui est « pas bon » dans les options ? (taille HUD in-game / loading screen / écran d'options lui-même / sliders)
- Sauvegarde 22:47 (truck bloqué) : F9 → vérifier FIX + TELEPORT dans le log ; Shift+F9 → truck sorti du hangar, pilotable
- Animation du dock (`hatch_end_anim`, `submarine_hatch_02_doorway_01_cin2`) : JOUE dans les deux états de l'option « Enable desktop cinematics » = comportement d'origine (liste de skip limitée à Drop_Pod/teleporter) — ajouter un skip dédié si demandé

## Session 27/10 22:27 — PDA sur la tablette + roue qui suit (fix 2 lignes, avis extérieurs)
User (test du 19:57, log 20:02) : PDA toujours invisible, roue figée dans le monde. Contrainte initiale « PDA : aucune modif vs commité/SN1 » → **retirée par l'user** (« je retire le ca marchait sans modifs ») après qu'il a demandé pourquoi c'était devenu si compliqué.

### Causes racines (agent de relecture + décompilation)
- **PDA** : le patch commité `uGUI_CanvasScalerPDA_Attach` fait un remap « monde → espace UI à l'origine » (`position = ancre − playerPos`). Sensé en SN1 (tête à l'origine) ; en BZ post-19:39 (uiCamera au player) l'écran tombe 242 m derrière. L'ancre PDA = bone `ScreenAnchor` sous `hand_L → PlayerPDA` (squelette du modèle) = la tablette dans la main du personnage.
- **Roue** : `VRQuickSlots.Activate()` place la roue UNE FOIS (pos du controller + LookAt tête) ; `Update()` ne fait que la sélection angulaire → plantée dans le monde en marchant.
- **Stéréo (C10) réglée par l'empirique** : le reticle validé en 19:39 prouve que la uiCamera rend depuis la position player (les matrices stéréo du jeu sont bien relatives au transform, ou la caméra est mono — les deux rendus observés sont cohérents). Aucune action.

### Architecture complexe écartée (P11)
Revert 19:39 + caméra overlay dédiée au player (CopyFrom main, mask UI, Depth, far 50 m, matrices stéréo miroir, reticle re-ancré, hit laser monde) — abandonnée : risque stéréo/clip/double-rendu, inutile une fois la contrainte PDA retirée.

### Avis extérieurs (2 IA sans accès au code) — convergences et correction
- **Fix PDA** : consensus « placement espace monde sur l'ancre ». La variante « exacte » (`+ worldRot × _spawnPosition`) **rejetée** (2 avis : valeurs capturées à OnEnable, risque d'obsolescence après reparent, le commité ne les utilisait jamais). La variante « relative » de l'avis 1 (`uiRig.position + (worldPos − playerPos)`) **rejetée** : `rigParentTarget` (parent jeu du mainCamera, capturé dans `ArmsController.Start` l.816) ≠ `SNCameraRoot.main.transform` (source du `playerPos`) → mélange de sources = jitter. La **variante minimale** (sans `playerPos` du tout) est la plus robuste.
- **Fix roue** : **l'avis 2 a pris un vrai bug de ma proposition** : `SetParent(uiRig)` ferait hériter la rotation de l'uiRig (qui suit la tête) → roue head-locked, orbite autour du visage, UX différente du commité (parallaxe naturelle). Remplacé par un **translation-follow** (position uniquement) avec ordre d'exécution pour zéro lag.

### Build 22:27 (271360 B)
1. **PDA** (`VRCameraRig.cs` `uGUI_CanvasScalerPDA_Attach`) : `position = _anchor.transform.position`, `rotation = _anchor.transform.rotation` (espace MONDE = l'écran sur la tablette, recalculé chaque frame car mode Inversed). Supprimé le remap à l'origine (`rigWorldPos`/`uiSpacePos`). Garde null `_anchor` + log one-shot debug `[PDAAttach] renderMode/worldCamera/pos` (confirme WorldSpace).
2. **Roue** (`VRQuickSlots.cs`) :
   - `[DefaultExecutionOrder(10)]` (après VRCameraRig −10 → lit `uiRig.position` de la frame courante, zéro lag au sea truck)
   - `wheelOffset` capturé à l'ouverture (après LookAt) : `transform.position − uiRig.position` (= pose main, exact)
   - `LateUpdate()` : `transform.position = uiRig.position + wheelOffset` tant qu'active → suit la translation du player (marche, véhicules), **sans** rotation de tête, **sans** suivi de la main (la sélection reste possible)
   - Jamais parentée → aucun couplage de durée de vie avec l'uiRig
3. Inchangé : 19:39 (uiRig suit le player), re-dirty Static, `DefaultExecutionOrder(-10)`, reticle = code SN1 + `AnchorOffset (0,0,0)`, AuditScaler, écritures gardées, menus World (pas de re-dirty World — jeux en pause, consensus des 2 avis).

### Tests user (ordre = criticité)
1. PDA : ouvrir, marcher, tourner la tête, onglets, boutons pause/photo/quickslots cliquables au laser — visible, sans nage
2. Sea truck rapide : PDA + roue stables (test de l'ordre d'exécution — le plus critique)
3. Roue : ouvrir, marcher (suit), tourner la tête (ne tourne pas), sélectionner en bougeant la main
4. Reticle sur cible : aucune régression, pas de dérive en marchant
5. Menu principal → jeu : pas de NRE, roue fonctionnelle
6. Mort/respawn + cinématique : PDA cohérent
7. Log : `[PDAAttach] renderMode=WorldSpace`, `[ScalerAudit]`, `[ReticleDebug]`

## Session 27/10 23:03 — slider « Hand reticle height » (texte trop bas)
Retour test 22:27 : **PDA corrigé ✓, roue corrigée ✓**, reticle réagit bien mais « le texte est trop bas, il faut le remonter par rapport à ma main » + l'user propose un slider dans les options.

### Calibration d'après les vieux logs (probe `[ReticleDebug]`, session 22:27)
- `root` == `anchor(controller)` : le texte est pile au centre du controller physique (offset 0)
- `hand(rightHand)` (bone du jeu, la main rendue) : **+5 à +20 cm au-dessus** du controller (médiane ~+17 cm ; 2 poses à -10/-15 selon les bras)
- → défaut **15 cm** + slider live

### Build 23:03 (271872 B)
1. `Settings.HandReticleHeightCm` (défaut **15**, cm, positif = haut) + slider « Hand reticle height (cm) » (-10..30, step 1) sous « Hand reticle mode » (onglet Experimental) — persistance auto `options.bin` du jeu (namespace `SubmersedVR/`), rien à ajouter à OptionsBootstrap
2. `ReticleBillboard.GetAnchorOffset()` = `AnchorOffset + (0, 0, -HandReticleHeightCm × 0.01)`. Utilisé au write par frame (mode TargetInfo, `localPosition`) → le slider agit en direct
3. `VRHud` : les 2 setups (OnHand, LaserPointer) utilisent le helper au setup + `ApplyReticleHeight()` (appelée par le callback du slider) pour les modes sans write par frame (legacy/on-hand)

## Session 27/10 23:14 — slider reticle (signe/range) + menus Static dé-head-locked
Retour test 23:03 :
1. Reticle : « il faut clairement une valeur négative. Le -10 est le minimum, il faut peut être -15 » → le défaut +15 va dans le mauvais sens pour la setup de l'user (l'inférence de signe « +z = -up de l'outil » était fausse pour sa pose) → calibration empirique fait foi.
2. **Effet de bord du re-dirty 19:57** : « le menu des options est maintenant fixé dans ma vue : si je tourne la tête il suit le mouvement, je ne peux pas regarder les bords » — le re-dirty re-exécutait le placement complet chaque frame AVEC la rotation live de la caméra (la tête) → panneaux head-locked. Spécifique user : jeu en pause au menu (player immobile, tête mobile). Demande : « suivre le player mais pas la tête ».

### Build 23:14 (272384 B)
1. **Slider reticle** : range étendue **-30..+30**, défaut **-15** (la valeur de l'user). Convention de signe inchangée (l'user a déjà calibré dans cette convention).
2. **`uGUI_CanvasScalerStaticTrack_Fixer` réécrit** (VRHud) : au lieu du re-dirty (placement complet = position + rotation live → head-locked) :
   - `Prefix` sur `OnUpdate` : enregistre `isDirty` AVANT que le jeu ne le clear
   - `Postfix` : si placement fraîche (`wasDirty`) → capture l'offset MONDE `rt.position − GetUICamera().position` (offset + rotation restent FIXES ensuite) ; sinon → ré-applique **seulement la position** `rt.position = uiCam.position + offset` (suivi translation du player : marche, véhicules ; rotation jamais ré-écrite = parallaxe tête comme le commité)
   - `GetUICamera()` = la caméra exacte du placement Static du jeu (même repère que la capture) ; `ManagedCanvasUpdate` publicisé (vérifié décompilé)
   - Re-ouverture / re-dirty du jeu (SetDirty, OnUICameraChange, enable) → `wasDirty` → re-capture ✓
   - S'applique aux Static actifs : menus (IngameMenu/options, builder), ScreenCanvas (HUD), etc. — tous retrouvent le comportement commité (fixes dans l'espace, parallaxe tête) + le suivi translation (plus laissés sur place en marchant)
   - Cas menu (jeu en pause) : player immobile → position fixe, rotation fixe → tourner la tête = parallaxe naturelle, bords lisibles ✓

## Session 28/10 00:16 — HUD véhicule : taille = à pied + position baissée (parité SN1)
Retour : « le HUD dans les véhicules est plus petit et légèrement trop haut » — dans les travaux écrasés, on l'avait corrigé (scale véhicule ≠ à pied → rectifié, puis baissé avec un offset).

### Diagnostic (logs + décompilation BZ)
- **Preuves logs** : à pied `phys=1,2x1,285 ls=0,001111` (frustum FOV 110°, dist 1 m) ; en SeaTruck `StaticHUD: sPhys=0,734x0,787 sLs=0,00068` → **1,63× plus petit** (0.00085×uiScale fixe vs frustum), et `localPos y=+0,1` → **0,1 m trop haut**.
- **Seamoth jamais re-parenté en BZ** : les patches `OnPilotModeBegin/End` ne testaient que `is Exosuit` depuis le 1ᵉ commit BZ (61c7d8d). SN1 avait `is SeaMoth || is Exosuit` (la référence « SN1 dans le seamoth »).
- **Il n'y a PAS de Seamoth dans BZ** (confirmé user 28/10) : la classe `SeaMoth` (décompilé `Assembly-CSharp.dll` 04/10/2026, `[Obsolete("This is an SN1 Vehicle, not used in BZ")]`) existe dans l'assembly (seule qui set `Player.inSeamoth`) mais le véhicule n'est pas dans le jeu. Véhicules BZ : prawn (Exosuit), SeaTruck, hoverbike « Snow Fox » (terre uniquement). La branche `is SeaMoth` (parité SN1) était donc morte → **retirée** le 28/10 (les 2 patches reviennent à `is Exosuit` seul).
- Le jeu BZ ne touche pas au HUD en véhicule (`uGUI` décompilé : aucun handler pilot) → tout est géré par le mod.
- `UpdateFrustum` (décompilé) : `_scaleX/Y = f(distance=1, MainCamera.FOV, écran, uiScale)` — le canvas statique figé à 0.00085 ne suivait pas ce scale.
- Fix perdu **non retrouvable dans git** : pickaxe `-S 'is SeaMoth'` vide sur tous les refs ; 8fded95 (fix/scales-hud) a le même ancien comportement → réimplémentation d'après mémoire user + parité SN1.

### Build 28/10 00:16 (271872 B)
1. **Scale véhicule = scale à pied** : `ApplyStaticHudScale()` copie `screenCanvas.transform.localScale.x` (le scale frustum courant, guard `> 0`) au lieu de `StaticHudBaseScale × uiScale`. Const `StaticHudBaseScale` supprimé + pin `0.00085` dans `Setup()` supprimé (la copie à la création + le sync par update suffisent).
2. **Position baissée** : `StaticHUDCanvas.localPosition` = `Vector3.forward` (0,0,1, parité SN1) au lieu de `Vector3.forward + (0, +0.1, 0)`.
3. **Branche Seamoth restaurée** : `is SeaMoth || is Exosuit` dans `SetHudStaticInVehicles`/`ResetHudStaticInVehicles` (pragma warning 618 autour des tests, [Obsolete] du jeu).
- Effet attendu : HUD véhicule (Seamoth, Exosuit, SeaTruck) = même taille physique qu'à pied (1.2 m avec canvasX/Y 0.45), centré (pas +0.1). Logs `StaticHUD:` : `sLs` doit montrer ~0.001111 (plus 0.00068), `localPos=(0,0,1)`, `hudOnStatic=True` en Seamoth.

## Session 28/10 09:31 — offsets HUD en cm (bug « HUD disparaît »)
Retour : « faire marcher les offset du hud dans les options : actuellement dès que je met autre chose que 0, le hud disparait, il faut que ce soit des cm ou équivalent ».

### Cause racine
- Les 2 canvases HUD (screenCanvas, StaticHUDCanvas) ont `anchorMin=anchorMax=(0,0)` et un **parent qui n'est pas un RectTransform** (uiCamera / uiRig) → dans ce cas `anchoredPosition` est en **MÈTRES** (repère local du parent), PAS en pixels du canvas.
- `ApplyHudOffset` écrivait la valeur brute du slider (plage ±500, « px ») dans `anchoredPosition` → « 10 » = le canvas part 10 m dans l'espace → HUD invisible.

### Fix (build 28/10 09:44)
1. `ApplyHudOffset` : l'offset est en **cm**, appliqué sur `localPosition` (physique, prévisible) : `lp.x/y = value/100`, z conservé. Valide pour les 2 canvases (screenCanvas + statique véhicule).
2. Sliders : « HUD Offset X/Y **(cm)** », plage **±100 cm**, step 1.
3. `OptionsBootstrap` : clamp des valeurs chargées du fichier à ±100 (les anciennes valeurs « px » pouvaient aller à ±500 = 5 m d'offset).
4. **Défaut Y = -10 cm** (défaut intermédiaire -5 testé en jeu, remplacé par -10 à la validation 28/10) : `Settings.HudOffsetY = -10.0f`, défaut slider `-10f`. Le fichier d'options user n'est **pas modifié** (demande user : « pas besoin de toucher mon bin d'option ») ; le défaut ne s'applique qu'aux sauvegardes sans la clé.
   - Piège PowerShell : `BitConverter.GetBytes(-5.0)` (double, 8 bytes) ≠ `GetBytes(-5.0f)` (float, 4 bytes) — la 1ʳᵉ réécriture manuelle avait écrit 0.0 par erreur (les 4 premiers bytes du double -5.0) ; le jeu a ensuite rechargé le fichier contaminé à la réouverture des options et ré-écrasé le -10 de l'user (09:42). Corrigé via `[float]$v = -5.0; GetBytes($v)` et vérifié au readback `ToSingle`.
5. **Le mod est en lecture seule sur le fichier d'options** (demande user 28/10 : « il ne faut pas réécrire les options à chaque lancement ») : suppression du « one-shot file repair » `LoadingScreenScale 3.0 → 1.0` dans `OptionsBootstrap` (réécrivait le fichier à chaque boot tant que la valeur était 3.0 — inerte chez l'user, fichier à 1.0, mais principe refusé). Chargement des offsets gardé : uniquement si la clé existe (clé absente → défaut code -5 conservé, pas de reset à 0).

## Session 28/10 09:51 — Loading screen scale : durci, retiré des options
Retour user : « le scale ne devrait pas etre dans les options : on agit en dur directement dessus au chargement ». Confirmé par questions : valeur **1.0**, périmètre **tout retirer**.
- `VRLoadingScreen.LoadingScreen_Patch` : nouvelle const `ScreenScale = 1.0f` (dure), appliquée au canvas au chargement (log + `ApplyScale`). Plus aucune lecture des options.
- Supprimés : champ `Settings.LoadingScreenScale`, const `HudDefaults.LoadingScreenScale` (1.5, devenu mort), lecture de la clé dans `OptionsBootstrap` (+ log), valeur dans le log snapshot `OptionsPersistence`. Pas de slider existait (la clé dans le fichier venait de l'ancienne migration 26/10, déjà retirée) ; la clé orpheline `SubmersedVR/LoadingScreenScale` restant dans le fichier user est ignorée par le jeu (comme `SeatruckRescueEnabled`), **fichier non réécrit** (principe lecture seule).
- `ApplyScale()` reste re-appliqué via `VRHud.ApplyHudScale` quand le facteur canvas change (compensation du drift) — le comportement n'est pas modifié, seule la source de la valeur change (const 1.0 au lieu du champ).

## Session 28/10 10:24 — HUD SpyPenguin figé sur le robot (à valider)
Retour user : « figer le hud du spypenguin sur le robot lui même ». Confirmé par questions : **devant le robot** (à distance fixe), **fixé au corps du robot** (la caméra vise → le panneau bouge dans la vue).
- Contexte BZ : `uGUI_SpyPenguin` = l'overlay plein écran du pilotage (bordre viewfinder, nom/statut/mode, distance, sliders companion/infiltration, aperçu photo) ; pendant le pilotage la vue du joueur = la caméra du pingouin (`SpyPenguin` déplace `SNCameraRoot` sur `cameraTransform`, axe de vue = `cameraTransform.forward` — raycast de visée), le player reste au poste de contrôle.
- Implémentation `Tweaks/SpyPenguinWorldHud.cs` (pattern PDA, PAS le pin world du loading screen qui a échoué) :
  - `SetPenguin.Postfix` : penguin ≠ null → **attach** ; null → **detach**.
  - Attach : le GO de l'overlay devient un canvas **WorldSpace autonome** (Canvas + `uGUI_GraphicRaycaster` ajoutés, rect explicite `sizeDelta` + anchors centrés, layer 0 comme le PDA), taille monde 1:1 avec l'affichage écran actuel (`rect.px × lossyScale`), reparenté sur un ancrage `SpyPenguinHUDAnchor` (DontDestroyOnLoad, pas enfant du pingouin → le panneau ne meurt pas avec lui en autodestruction).
  - `LateUpdate.Postfix` (driver frame par frame) : l'ancille suit le pingouin — `pos = penguin + up*0.8 + forward*1.5` (m), `rot = penguin × 180°Y` (face du canvas vers la caméra ; **à caler au 1er test** : distance/hauteur/sens du flip).
  - Detach : reparent au parent d'origine, restauration complète (anchors, offsets, local pos/rot/scale, layer), composants retirés.
- Risques connus : (1) l'axe/flip à valider in-game (log `SpyPenguin world HUD attached:` donne px→monde + parent d'origine) ; (2) sliders interactifs à distance : portée du laser côté jeu (non documentée dans le mod), à valider au test ; (3) la Seaglider (autre drone) reste head-locked (scope = SpyPenguin uniquement).

### Test 1 (10:2x) + fix build 10:xx
Retour user : « le hud est bien lié au robot, avec plus ou moins les bons éléments ; par contre il n'est pas assez large, il a été fortement réduit et ça cache pas mal d'info » + clarification : « **le même HUD qu'avant, juste lié au robot plutôt qu'à la caméra** ».
- **Bug trouvé au log** (`px=(0x0) world=(1,2x0) scale=0,001` : le GO « HUD » est stretch-anchored sous `ScreenCanvas` (sizeDelta (0,0), rect résolu 1200px) ; j'avais changé les anchors **avant** de lire `rect.size` → le rect s'effondrait à 0×0 → `sizeDelta=(0,0)`, les enfants stretch (feed, cadre) réduits → info masquée. Fix : capture `onScreenSize = rt.rect.size` **avant** tout changement d'anchors.
- **Taille** : const `PanelScaleMultiplier` (test 1 : 1,3×). Constantes calibrables : `PanelForwardDistance` (1,5 m), `PanelHeight` (0,9 m), `PanelScaleMultiplier` (1,0).

### Test 15 (15:0x) + build 15:03 — toujours invisible ; `SNCameraRoot.main` n'est PAS fiable — **MIS DE CÔTÉ**
Retour user : « toujours pas » → **bandes mises de côté** (décision user), reprise ultérieure.
- **Log 15:03** : **zéro ligne `SpyPenguin plane re-parented`** (le re-parent n'a JAMAIS eu lieu) ; diag `plane: parent=MainCamera(inst=49616) ... cam=MainCamera(inst=50170)` (mêmes inst que le test 13, stables entre sessions) ; coins monde bande gauche = (-228,12 / -227,88 ; y 1,63→7,61 ; z 176,6→177,0) vs camPos=(-226,76, 4,65, 175,77) → **la bande est 3,02 m SOUS la cam pilotage** → -73° hors champ (invariable depuis le test 11 : le plan n'a jamais quitté 49616).
- **`render-time #1..2 cam=ImguiCamera(inst=50174) eye=Both n/a`** : frustum `n/a` → au moment de `willRenderCanvases`, `cam.projectionMatrix` n'est PAS encore la matrice de la caméra courante (garde `|P[2,3]|>0,5` échoue) → **mesure du frustum par willRenderCanvases = impasse** (il faudrait un autre hook, ex. onPreCull ou les matrices stéréo de `SNCameraRoot.UpdateVR`).
- **État des bandes au rendu** (vu de l'ImguiCamera à l'origine) : gauche midMin=-52,3° midMax=-52,2°, droite midMin=-52° midMax=-51,9° → la géométrie SUR LE PLAN est correcte (±52° = atan(1285,6) ✓ comme conçu) ; le plan est juste à l'ancienne position caméra.
- **Décompilation `SNCameraRoot`** (clé pour comprendre) : `public static SNCameraRoot main` = **champ statique écrit dans `Awake()` (`main = this`) et effacé dans `OnDestroy()`** → le dernier instance réveillé gagne, un 2nd rig le flippe ; `mainCam => mainCamera` (champ `[SerializeField] private`, stable par instance) ; 3 caméras par rig (mainCamera/guiCamera/imguiCamera, les 2 dernières détachées à l'origine dans `Start()`).
- **Notre propre mod est mêlé** : `VRCameraRig.cs:495` `rig.StealCamera(SNCameraRoot.main.mainCamera)` → c'est NOUS qui reparentons la main caméra du jeu sous VRCameraRig (d'où le path `VRCameraRig/MainCamera/SpyPenguinBorderPlane/...` du plan, parent 49616) ; `AudioFix.cs` patche `SNCameraRoot.Awake` → plusieurs instances peuvent coexister.
- **PARADOXE (à trancher à la reprise)** : `EnsurePlaneCamera()` (driver, garde Unity-null `(object)main != null`) et `LogBandDiag` (garde référence `main != null`) lisent la **même expression** `SNCameraRoot.main.mainCam` à quelques μs d'écart, même frame : le 1er n'a JAMAIS re-parenté (→ a lu 49616 ou null/zombie), le 2ème a lu 50170. Hypothèse : `SNCameraRoot.main` est un **zombie** (instance détruite → Unity-null mais référence vivante) dont le champ `mainCamera` pointe encore la cam live 50170 — notre garde Unity-null **skippe silencieusement** le zombie. Ou le statique flippe entre les 2 lectures. À confirmer par un log par frame de `SNCameraRoot.main.GetInstanceID()` + état Unity-null + `mainCam` (et des 2 cam inst).
- **Doute sur la cible elle-même** : faut-il suivre `SNCameraRoot.main.mainCam` (cam jeu, 50170) alors que la vue du user en VR passe par le rig headset (VRCameraRig / cam yeux) ? Le plan devrait peut-être suivre la **caméra de vue réelle du VR rig**, pas la mainCam du jeu.
- **État code** : build 15:03 (292864 B) déployé ; `EnsurePlaneCamera` + ZTest Always + render-check détendu sont en place et inoffensifs (bandes restaurées proprement au detach, Fader opérationnel) ; rien à réverter.

### Test 14 (15:0x) + build 15:03 — plan suit la mainCam courante + ZTest Always (plan relu par agent)
Fix des TODO bandes #1-3 (plan `docs/superpowers/plans/2026-10-28-spyPenguin-band-camera-fix.md`, verdict agent « APPLICABLE », 1 correction mineure appliquée : garde ortho `|P[2,3]|>0,5` — m32 = -1 en perspective, 0 en ortho ; `P[1,1]` est non nul dans les deux).
- **Fix 1 (racine, TODO #1)** : `EnsurePlaneCamera()` appelé **chaque frame** dans le driver (entre `EnsureBorderBands` et `SyncBandAlpha`) : re-parente le plan à `SNCameraRoot.main.mainCam` **actuelle** si `parent != cam.transform` (log `SpyPenguin plane re-parented: <old> -> <new> - mainCam swapped`), recrée le plan s'il a été détruit avec son ancienne caméra (fake-null → `EnsureBandPlane`).
- **Fix 2 (TODO #2)** : matériau `UI/Default` custom + `unity_GUIZTestMode = Always` (`UnityEngine.Rendering.CompareFunction.Always`) sur les Images des bandes au collect (original material sauvegardé + restauré au Detach) — le ZTest `LEqual` masquait les bandes derrière la géométrie du cockpit à < 1 m off-axis. Le Fader couvre toujours les bandes (sortingOrder canvas ; l'UI n'écrit pas le depth).
- **Fix 3 (TODO #3)** : `OnWillRenderCanvases` détendu — **n'importe quelle caméra**, 2 fires max : log `SpyPenguin render-time #N cam=<name>(inst=) eye=...` + coins de frustum (garde perspective `|P[2,3]|>0,5`, `n/a` sinon) + état des bandes au rendu (fire #1) → détecte l'écrivain tiers ET identifie les caméras qui rendent les canvases en VR (test 13 : 3× `ImguiCamera(inst=50174)`). Retraits : `renderCheckCam`, `renderCheckMissLog` ; helper `UnhookRenderCheck`.
- **À lire au prochain test** : (1) bandes visibles des DEUX côtés (bord sombre collé au bord de vue, tunnel depuis 40°) ; (2) `plane re-parented: MainCamera(inst=49616) -> MainCamera(inst=50170)` ; (3) `band BorderLeft/Right: midMin/midMax ≈ -60/-40 et +40/+60` ; (4) `render-time #1..2` → identité des caméras + frustum réel par œil ; (5) sortie : Fader noir couvre les bandes, texte de connexion visible.

### Test 13 (14:3x) + build 14:34 — RAISON RACLELLE : le plan est parenté à la MAUVAISE caméra
Retour user : « **toujours pas de bordure visible** » (les 2 bandes désormais sur le plan) + « on va stopper là, fais ton analyse pour les TODO ».
- **Le log 14:34 tranche** (nouvelles lignes monde) : `plane: parent=MainCamera(inst=49616)` MAIS `cam=MainCamera(inst=50170)` → **DEUX GO nommés « MainCamera »** : la caméra à l'attach (49616) ≠ la caméra à la diag (50170 = `SNCameraRoot.main.mainCam` du moment). **`mainCam` est réassignée au démarrage du pilotage** (la référence change entre le postfix `SetPenguin` et les frames suivantes) → le plan, parenté à la caméra de l'attach, reste **accroché à l'ancienne caméra**.
- **Preuve numérique** (les coins monde loggués) : bande gauche `midMin=(-220,18,-2,1,74,72)`, `camPos=(-218,84,1,04,74,79)` → vecteur ≈ (-1,34, **-3,14**, -0,07) : la bande est **3,14 m SOUS la vue pilotage** (au niveau du plancher/position de contrôle), ~1,3 m à gauche, à ~0 m de profondeur → **-73° bas-gauche, hors vue** ✓✓ (le -73° des tests 11/12 s'explique EXACTEMENT — ce n'était pas un bug de diag, la bande était réellement là). Idem 2ᵉ session (Δy = -3,19 m, yaw robot 199°).
- **Conséquence** : « c'est bon » du test 11 = la bande DROITE native (restée sur le ScreenCanvas, head-locked correct) seule visible ; la gauche sur le plan était déjà invisible. Test 13 : les 2 bandes sur le plan → les 2 à 3 m sous la vue → « plus aucune bordure ».
- **Autres faits nouveaux du log 14:34** :
  - `BorderRight` est **miroité dans le prefab** : `localScale=(-1,1,1)` (BorderLeft = (1,1,1)) → la politique « garder le localScale d'origine » est la bonne ; `lossyScale=-0,001` confirmé.
  - `render-check miss #1..3: Camera.current=ImguiCamera(inst=50174)` → en VR les canvases uGUI sont rendus par l'**ImguiCamera**, pas la MainCamera → le snapshot `willRenderCanvases` visait la mauvaise caméra (et les bords stéréo réels n'ont toujours pas été mesurés).
  - **Le jeu re-layoute les bandes entre sessions** : offsets d'origine `0,0..300,0` (1ᵉʳ attach) vs `150,0..450,0` (2ᵉ attach, après detach+restore) → la restauration au detach est best-effort (le jeu repart de sa propre mise en page au pilotage suivant) — ne pas chercher à « gagner » ce combat.
  - Les 2 instances fantômes (12980/16368 sous `uGUI/ScreenCanvas/HUD/...`) toujours `active=False` : un 2ᵉ uGUI jamais actif, inoffensives.
- **TODO bandes (à reprendre quand on y revient)** :
  1. **[racine] Re-parenter le plan à la caméra ACTUELLE chaque frame** : dans le driver, appeler `EnsureBandPlane(SNCameraRoot.main.mainCam.transform)` **inconditionnellement** (pas seulement à la collecte) — sa clause `if (bandPlane.transform.parent != camT) SetParent` fera le swap dès que `mainCam` change (c'est elle qui a créé le bug : le swap de `mainCam` n'était géré que si une NOUVELLE bande était collectée).
  2. **[risque] ZTest** : une fois sur la bonne caméra, le plan est à 1 m devant la caméra du robot — si la géométrie du cockpit/du corps du pingouin (latéraux < 1 m) masque les bandes à ±40-60° (ZTest `LEqual` du shader UI), matériau `UI/Default` + `SetInt("_ZTest", (int)CompareFunction.Always)` sur les Images des bandes (avis 2 externe l'anticipait). Vérifier au test avant de l'appliquer.
  3. **[diag] Snapshot au rendu** : viser l'**ImguiCamera** (ou `camera.onPreCull` par caméra) pour le snapshot « écrivain tiers » + la mesure des **bords de frustum réels par œil** (toujours non mesurés → calibrage 40/60 sur le nominal `camFov=110`).
  4. **[cosmétique] Restauration detach** : le jeu re-layoute les bandes de toute façon (150..450 vs 0..300) — documenter comme attendu, ne pas « corriger ».
  5. État actuel du code : `EnsureBorderBands` idempotent par frame ✓, placement caméra-space ✓, alpha sync ✓, sorting sous le ScreenCanvas ✓ — seule la **persistance du parent du plan** (TODO 1) empêche la visibilité.

### Test 12 (14:2x) + fix build 14:34 — collecte fiable des 2 bandes + diag monde
Retour user (build 14:21 testé) : « **c'est bon** » + commit demandé (`55b0bf1`) + « analyse aussi le log ».
- **Log : la gauche est sur le plan, la DROITE non** : `band instances:` → `BorderLeft [OURS]` sur `VRCameraRig/MainCamera/SpyPenguinBorderPlane` (localPos -1285,6 ✓) MAIS `BorderRight inst=-2354 active=True` **toujours sur le ScreenCanvas** (`uiRig/MainCamera (UI)/ScreenCanvas/Border/BorderRight`) + le tree head-locked confirme `Border(2916x3124,3)[BorderRight(300x3124,3)[]]`. → la bande droite vue par l'user était la **NATIVE 300 px** (sur le canvas stretché 2,85×, qui tombait près du bord de vue) — « c'est bon » en partie chanceux. Cause : à l'attach, `rect.width < 10` de BorderRight (layout pas résolu au 1ᵉʳ frame) → sautée par la garde `w < 10f`, et le self-heal ne retryait que si `bandChildren.Count == 0` (compteur = 1). Les 2 autres instances (12980/16368, sous `uGUI/ScreenCanvas/HUD/...`) sont **inactive=False** = un 2ᵉ uGUI jamais actif, inoffensives.
- **`midMin=-73° midMax=-73°` (gauche)** : impossible par construction (un point enfant de la caméra à local (-1,286,0,1) est à 52° de celle-ci) → bug du DIAGNOSTIC, pas du placement (l'user confirme visuellement « c'est bon »). À trancher avec les nouvelles logs monde/caméra.
- **Pas de lignes `stereo eye=` ni `band render-time`** : `Canvas.willRenderCanvases` n'a jamais feu avec `Camera.current == mainCam` (en VR le canvas passe par un autre pass/`Camera.current` nul) → le snapshot au rendu est inopérant tel quel.
- **Fixes build 14:34** : (1) `EnsureBorderBands` **idempotent** (skip des bandes déjà collectées, PAS de `Clear`) + **sans garde de largeur** + **re-exécuté chaque frame par le driver** (boucle ~2 enfants, coût nul) → les 2 bandes collectées dès que posées, quel que soit le timing du layout ; (2) log d'état d'origine de chaque bande **à chaque collecte** (plus seulement 1ᵉʳ session — une collecte tardive doit logger pivot/miroir) ; (3) diag enrichi : **instanceID + pose de la caméra** + **instanceID du parent du plan** (vérif same-object caméra/plan) + **coins des bandes en MONDE** (cross-check numérique) ; (4) render-check : log (cap 3) du nom/instanceID de `Camera.current` quand ça ne matche pas → trouver qui rend les canvases en VR.
- **À lire au prochain test** : `band instances:` → les 2 bandes `[OURS]` sur le plan (plus aucun `active=True` sur le ScreenCanvas) ; `cam(...)` vs `parent(...)(inst=)` → même instanceID ? ; `midMin=(x,y,z)` monde vs `camPos/camRot` → les -73° expliqués ; `render-check miss #1..3` → identité du pass de rendu VR.

### Test 11 (14:1x) + build 14:21 — plan ENFANT DE LA CAMÉRA (avis externes)
Retour user (build 13:43 testé) : « **toujours pas bon, encore moins bien placé qu'avant : il est reparti vers la gauche, son bord droit devient encore plus visible alors qu'on devait l'étendre (de 55 à 60)** ». + question des écrans inclinés (non : 1-3° symétriques vs 15-25° d'asymétrie mesurée) → 2 avis externes demandés.
- **Analyse des logs (13:43)** : `rays=(ok,ok)` sur les 2 bandes, ratio `localX` = 1559,3/755,4 = 2,064 = tan60/tan40 ✓ (le calcul rayon-plan était correct), mais les coins mesurés (≈1590-1780 px locaux, largeur ~200 px) = **l'état PREFAB d'origine des bandes (300 px au bord du canvas)** — ni 1200 px (init attach), ni 755-1559 (rayons). Donc même l'écriture de l'attach n'apparaissait pas dans l'état mesuré → famille de causes « mauvais objet (clone) / écrivain tiers / re-instantiation » (confirmée par les 2 avis : les setters uGUI sont synchres, pas de « rect stale »).
- **Avis 1** : son « smoking gun » xMin>xMax était inexact pour notre code (on fait `Mathf.Min/Max`), MAIS : ne jamais écrire `offsetMin/Max` par script → `localPosition + sizeDelta` (largeur toujours positive) ; vérifier `localScale.x`/pivot des bandes. **Avis 2** : diagnostic tranchant d'abord (3 temps + état complet), plan **enfant de la caméra** (pas copie par frame = course au LateUpdate du head-lock), ZTest/height/sorting si plan dédié, et **en XR `camFov` est nominal — les vrais bords viennent des matrices de projection par œil**.
- **Implémentation (1 build = fix + diagnostic)** :
  - **Plan `SpyPenguinBorderPlane` ENFANT DE `mainCam`** : canvas WorldSpace, `localScale 0,001` (1 px = 1 mm), `sizeDelta 6000×6000` (±71° verticaux, couvre les casques ±50-55°), `localPosition (0,0,1,0)` + rotation identité → suivi de tête **exact par hiérarchie** (zéro copie de pose par frame). `overrideSorting=true`, `sortingOrder = screenCanvas.sortingOrder - 1` → le Fader noir (screen canvas) reste au-dessus des bandes à la sortie. Re-créé si le jeu détruit la caméra (level reload) ; désactivé au detach.
  - **Bandes re-parentées sur ce plan** à l'attach (`CollectBorderBands` remplace `WidenBorderBands`) : état complet sauvegardé (parent, siblingIndex, ancres, offsets, **pivot**, localPos/rot/scale, **layer**) → restauration exacte au detach (sibling index croissant).
  - **Placement caméra 1 fois à l'attach** (`PlaceBorderBands`) : `anchorMin=Max=(0,5,0,5)`, `pivot=(0,5,0,5)`, `localPosition=(centerX,0,0)`, `sizeDelta=(width,6000)` avec `x = tan(angle)×1000 px` (x40=839, x60=1732, width=893) — largeur **toujours positive**, le plan suit la caméra donc position constante pour la session. `localScale` d'origine **conservé** (si le prefab est miroité, le log one-shot le dira → flip éventuel au test suivant).
  - **Alpha** : `SyncBandAlpha` par frame — `planeGroup.alpha = hud.exploreBorder.alpha` (le jeu anime ce CanvasGroup chaque frame, uGUI_SpyPenguin L336 ; les bandes ne sont plus sous son groupe).
  - **Self-heal** : le driver re-collecte tant que `bandChildren.Count==0 && borderRt==null`.
  - **Diagnostic one-shot enrichi** (`LogBandDiag`, caméra sur le robot) : état **complet** du plan + de chaque bande (instanceID, **chemin complet** → détection de clone, pivot, lossyScale, rotation, valeurs écrites) + **tous les GO `BorderLeft`/`BorderRight` de la scène** (un 2ᵉ instance = celui que l'user voit) + **bords de frustum réels par œil** (à temps de rendu : inversion de `projectionMatrix × cameraToWorldMatrix`, coins du plan proche (±1,±1) projetés en monde — gère le frustum XR décalé par œil, `camFov` nominal en plus) + **snapshot au rendu** (`Canvas.willRenderCanvases`, 3 fires pour les 2 yeux) : si le rect au rendu ≠ les valeurs écrites → écrivain tiers entre LateUpdate et rendu.
  - `GetStereoViewProjectionMatrix`/`m04` indisponibles dans les refs Unity compilées → remplacé par la méthode coins-de-frustum ci-dessus.
  - Feed du pingouin : pas de caméra monde (vérifié en décompilé — `photoPreviewImage` = photo capturée seule) → le plan enfant de mainCam ne contamine rien d'autre.
- **À lire au prochain test** : (1) lignes `SpyPenguin band BorderLeft/BorderRight: ... midMin/midMax` → attendu **±40°/±60°** sur les deux bandes (le plan est perpendiculaire à la vue par construction) ; (2) `band instances:` → une seule instance par nom `[OURS]` (sinon clone trouvé) ; (3) `band render-time ...` → identique aux valeurs écrites (sinon écrivain tiers) ; (4) `stereo eye=... frustum corners ...` → les vrais bords de vue par œil (calibrage de 40/60 si besoin) ; (5) visuel : dégradé sombre collé au bord de la vue **des deux côtés**, tunnel depuis 40°, texte de l'écran de sortie toujours visible.

### Test 10 (13:3x) + build 13:43 — bandes 40/60 + diag rayons
Retour user (build 13:34 testé) : « **l'écran noir de sortie c'est bon** » (fix ordre de rendu validé ✓) ; bandes : « **la bande de droite est pas mal mais à étendre un peu plus, la bande de gauche non visible (étrange cette asymétrie) → passe plutôt les bandes de 40° à 60° (au lieu de 45/55)** ».
- **Asymétrie confirmée au log** (13:34) : `band BorderRight: 59,8°/64,4°` (3D, coins haut/bas — la ligne médiane ≈ 45°/56° ✓ bien placée, d'où « pas mal ») mais `band BorderLeft: -67°/-62,4°` = **sa position initiale (1200 px au bord du canvas, HORS de la vue)** → le placement ne l'a JAMAIS mise à jour. Hypothèse : rayon gauche sans intersection valide (t hors garde) → `continue` → position initiale conservée. La cause exacte reste à prendre sur le vif.
- **Fix user spec** : `BorderInnerAngle` 45 → **40**, `BorderOuterAngle` 56 → **60** (tunnel plus large ; le 60° passe 5° au-delà du bord de vue → marge tête accrue).
- **Diag `LogBandAngles` enrichi** (one-shot) : (a) **pose du plan** — pivot du border relatif à la caméra (x,y,z), son angle dans la vue (`pivotAngle`) et l'obliquité du plan vs l'axe de vue (`oblique`) ; (b) **par bande** : `rays=(ok/FAIL,ok/FAIL)` + distances de rayon `t` + `localX` des deux points d'intersection (attrape un rayon gauche en échec) ; (c) **angles de la ligne MÉDIANE** des bandes (midpoints des bords verticaux) au lieu des coins haut/bas — les coins sont à ±1,4 m de la hauteur caméra (bande ~2,8 m de haut) et gonflaient les angles 3D mesurés (59,8/64,4 au lieu de ~45/56 en réalité).
- **À lire au prochain test** : la ligne `plane pivot=... pivotAngle=... oblique=...` + `rays=(ok,ok)` attendu sur les DEUX bandes (si le rayon gauche = FAIL → la garde t est trop stricte ou le plan est plus oblique que prévu → ajuster les gardes / la méthode).

### Test 9 (13:2x) + fixes build 13:34 — rayon-plan + ordre de rendu
Retour user (build 13:17 testé) : « **presque bon**, mais différence œil droit/gauche : j'arrive à deviner le border à droite, celui de gauche reste invisible » + question sur la largeur 1200 px (non, la largeur est la zone 45°→56° ≈ 0,5 m) + « **l'écran noir qui apparaît quand on quitte le robot n'a plus le texte dessus, surement un 180° de trop aussi** ».
- **Bug bandes trouvé au log** (build 13:17) : `band BorderLeft: outer=-67° inner=-62,4°` / `band BorderRight: outer=+65,6° inner=+64,4°` — les bandes étaient à **±62-67° (hors du champ ±55°)** et écrasées (droite ~0,1 m) au lieu de ±45/56°. Cause : le placement projetait la coordonnée **x locale** du point monde attendu sur le plan (z=0) — équivalent à l'angle voulu **uniquement si le plan est perpendiculaire et centré** sur la vue ; ici le plan canvas est fortement oblique/décalé (head-locked tête vs axe de visée du robot) → erreur latérale ~0,7-0,9 m.
- **Fix 1 : placement par intersection rayon-plan** : pour chaque bord de chaque bande (±45°/±56°), rayon = `cfwd` tourné de l'angle autour du `up` caméra (`AngleAxis`), intersection avec le plan du Border (`normal = borderRt.forward`, gardes `|dot(dir,n)| ≥ 0,01` et `t ∈ [0,05..10]`), point local = `InverseTransformPoint` (sur le plan par construction → exact quelle que soit la pose). Côté sombre : comparaison des x locales **réelles** des points outer/inner → flip X si l'ordre est inattendu (gère oblique ET miroir, remplace la détection par signe). `ViewRayPlanePoint` (helper). Largeur naturelle = la zone angulaire (~0,5 m), pas 1200 px.
- **Bug écran noir sans texte** : pas un 180° — un **ordre de rendu**. `SetPenguin(null)` (sortie du robot, `SpyPenguin.DisablePenguinCam` L416) active l'écran `connecting` (noir + texte « Connecting... ») + force le `Fader` (noir plein écran, se lève après 0,5 s **et `IsWorldSettled()`** — plusieurs secondes). En vanilla le `connecting` est rendu **au-dessus** du Fader. Nos re-parentings attach→detach (`SetParent` = toujours **append en fin de liste d'enfants**) replaçaient Fader/Border/... **après** `Connecting` dans la section `SpyPenguin` après le 1ᵉʳ cycle → le Fader noir masquait le texte.
- **Fix 2 : restauration de l'ordre de rendu** : `origSiblingIndex` (panneau) + `FilterChild.siblingIndex` (`GetSiblingIndex()` à l'attach) ; au detach, `SetSiblingIndex(index original)` après chaque `SetParent` — **restauration triée par index croissant** (SetSiblingIndex n'est exact que si les frères d'index inférieur sont déjà restaurés). L'attach/detach restaure désormais la hiérarchie **exactement** (parents, ancres, offsets, layers, canvas, ordre des frères).
- **En attente de test** : (1) log `band check` → `outer ≈ ±55-56°`, `inner ≈ ±45°` sur les **deux** bandes ; (2) dégradé sombre collé au bord de la vue des deux côtés, tunnel 45°→bord ; (3) à la sortie du robot : écran noir **avec** son texte ; (4) un cycle attach/detach ne modifie plus l'ordre des GO.

### Test 8 (12:4x→13:0x) + placement view-space build 13:17
Retour user (build 12:43 testé) : « pas bon, **le dégradé de droite commence presque au milieu de l'écran** ... tu as surement raté ton ancrage » ; précision : « **c'est le bord droit de l'image** (là où c'est le plus noir) **qui commence au milieu de l'écran** : le reste du dégradé s'étale vers la gauche comme attendu. Donc c'est surtout que **le bord droit de l'image doit être sur le bord droit de la vue** » ; puis la spec : « champ de vision ~110° par les deux yeux, **zone de recouvrement (3D) = 90°** → le border doit cacher au moins la zone au-delà des 90° → **effet tunnel** ».
- **Log band check (build 12:43)** : `camFov=110 (view edges ±55°)` ; `BorderLeft: dist=1m outerAngle=-67,8° innerAngle=-58,9°` (bande entière HORS de la vue à gauche) ; `BorderRight: dist=1m outerAngle=-62,7° innerAngle=+61,7°` (bande traversant TOUTE la vue). → **le plan du canvas est oblique/décalé** par rapport à l'axe de la vue → ancrer les bandes aux bords du canvas ne les positionne PAS dans la vue (1200 px = 1,33 m à 1 m ≈ 74° → le bord transparent à ~16° du centre = « commence au milieu de l'écran »).
- **Solution : placement view-space par frame** (`PlaceBorderBands`, build 13:01) : depuis la caméra rendue (`SNCameraRoot.main.mainCam`), `d` = distance cam→plan du border (guard 0,3..5 m), `axisPoint` = intersection de l'axe de vue et du plan ; le **bord sombre** à `±BorderOuterAngle = 56°` (bord de vue ±55° + 1° de marge tête), le **bord transparent** à `±BorderInnerAngle = 45°` (zone de recouvrement 90°) → projeté via `InverseTransformPoint` sur le RectTransform du `Border` (fonctionne quelle que soit la relation canvas/vue). `WidenBorderBands` ne fait plus que l'init de la largeur (4×, 1ʳᵉ frame) ; restore des ancres + `localScale` au Detach.
- **Revue critique (agent) → fixes build 13:17** :
  - **[majeur]** le GO HUD porte déjà un Canvas (tree log racine `,Canvas`) → son état d'origine (renderMode/worldCamera/enabled) n'était pas restauré après le 1ᵉʳ detach → le panneau head-locked aurait pu être cassé (WorldSpace orphelin). Sauvegarde à l'attach (`origRenderMode`/`origCanvasWorldCamera`/`origCanvasEnabled`) + restauration au detach.
  - `Quaternion.LookRotation(cfwd)` → **up explicite** (le vecteur right dégénère quand la vue est quasi verticale : servo ±75° + inclinaison tête) — dans `PlaceBorderBands` ET `LogBandAngles`.
  - **Détection miroir explicite** (`InverseTransformDirection(cright).x`) + flip X de la bande si miroité (sinon le dégradé sombre se mettrait vers l'axe de la vue) — l'ancien swap ne faisait qu'ordonner l'intervalle, pas l'orientation de la texture.
  - `WidenBorderBands` : ne traite QUE `BorderLeft`/`BorderRight` (tout autre enfant du `Border` laissé intact).
  - Retry attach : check width **et** height (sinon LogWarning chaque frame dans l'état dégénéré).
  - `SetPenguin_Patch` + diag : pattern Unity-null (fake-null).
  - Commentaires désuets alignés (2,85× et non 2,7× ; le jeu saute le copy au 1ᵉʳ frame ; 4× = largeur initiale).
- **Note d'architecture (revue)** : le rig VR copie la chaîne caméra **avant** que le jeu pose le root (`SpyPenguin.LateUpdate`) → la vue retarde le panneau d'1 frame (~10-15 cm de décentrement en vol rapide). Architecture globale (idem PDA), pas un bug de ce fichier.
- **En attente de test** : lignes `band check` — `outerAngle` ≈ ±55-56° ET `innerAngle` ≈ ±45° sur les **deux** bandes (bord sombre = bord de vue, tunnel depuis 45°) ; dégradé sombre à l'extérieur des deux côtés ; cycle pilotage → libérer → repilotage (restauration head-locked = fix canvas/layers) ; slider de hauteur en direct (caméra + panneau ensemble).

### Test 7 (12:3x) + fixes build 12:43
Retour user : « c'est mieux ; **le border de droite n'est pas collé au bord de l'écran** ; **celui de gauche à peine visible, « peut-être trop à gauche » — vérifier que son bord gauche est collé au bord gauche de l'écran, si oui élargir encore ×2 à 1200 px pour le voir, on pourra le réduire** ».
- **Cause probable du décalage** : l'overscan 0,65 (2,3× le canvas) donne un bord du plan à ~54° alors que la vue va à 55° → **~1° de gap** de chaque côté (« pas collé au bord »).
- **Fixes build 12:43** :
  - `FilterFullViewOverscan` 0,65 → **0,85** (2,7× le canvas → bords du plan HORS de la vue → bandes visuellement collées au bord de la vue).
  - `BorderBandWidthMultiplier` 2 → **4** (bandes 300 → **1200 px**, les deux, symétrique — demande user pour la gauche, « on pourra le réduire »).
  - **Diagnostic angles** (`LogBandAngles`, one-shot, 1ʳᵉ frame driver où la cam est déjà sur le robot — le copy du jeu dans `SpyPenguin.LateUpdate` n'est pas ordonné vs notre patch au moment de l'attach) : `SpyPenguin band check: camFov=... (view edges ±...)` + par bande `dist=...m outerAngle=...° innerAngle=...°` (bord extérieur/intérieur par rapport au centre de la vue ; le collage = outerAngle ≥ ±camFov/2). `AngleFromCamera` (Atan2 du perp au forward, signe via la droite caméra).
- **En attente de test** : lignes `SpyPenguin band check/band BorderLeft/BorderRight` du log → vérifier le collage réel (et l'asymétrie gauche/droite perçue) ; réduire la largeur après.

### Test 6 (12:0x) + fixes build 12:32
Retour user : « il faut une hauteur de 40cm ; **il manque toujours des infos dans la TV** ; **la TV est à l'envers (face arrière)** — certains éléments invisibles (ceux avec du texte), d'autres visibles ; le « fader » = **un panneau plat avec grain + dégradé noir sur les bords, bien fixé à la caméra (OK)** mais **trop petit et carré → doit prendre le champ de vision complet, « full screen sur chaque œil ? »**.
- **`head-locked subtrees:` (log 12:07)** : `Border => Border(1080x1157)[BorderLeft(300x1157)[] | BorderRight(300x1157)[]]` — le « fader » = les 2 bandes dégradé gauche/droite (taille fixe, ne suivent PAS le rect du Border). `SelfieCanvas` = bordure selfie (BorderA/B, flakes, sparkles) 2400×1350.
- **TV à l'envers → cause des infos manquantes** : le flip Y180° préventif montrait la FACE ARRIÈRE du panneau (images UGUI Cull Off = visibles mais miroitées ; texte TMP Cull Back = invisible) → **flip retiré** (`anchor.rotation = t.rotation`). Convention vérifiée : le ScreenCanvas (lisible, aligné caméra) et l'attach PDA (`rotation = worldRot` de l'os ScreenAnchor, sans flip).
- **Plan validé par l'user** : border calé au **bord extrême** de la vue, **2× plus large** (300→600 px) pour que le dégradé soit progressif ; hauteur défaut **40 cm**.
- **Implémenté (build 12:32)** : `AddFilterChild(exploreBorder, fullView:true)` (overscan 0,65) ; `WidenBorderBands` : BorderLeft/BorderRight forcées en ancres de bord (x=0 / x=1, pleine hauteur) avec largeur ×2 (`BorderBandWidthMultiplier = 2.0f`), save/restore `BandChild` dans Detach, log one-shot ancres+offsets+`imageType` (stretch du dégradé à vérifier) ; `Settings.SpyPenguinHudHeightOffset = 40f` + slider défaut 40 (valeur sauvegardée fichier = prioritaire).
- **« full screen sur chaque œil »** : 1 seul plan head-locked — la caméra stéréo le rend dans chaque œil.
- **En attente de test** : TV lisible (distance ?), cadre noir aux bords (largeur/progression du dégradé), hauteur 40.

### Test 5 (11:4x) + fixes build 11:59 / 12:07
Retour user : « fader legacy OK (1ʳᵉ ouverture) ; fader robot **toujours trop petit (pareil qu'en legacy)** ; **HUD incomplet, mal placé (trop bas), il manque pas mal d'indicateurs** ».
- **Log 11:43 → 3 mystères résolus** :
  - `filter orig Fader: parent=SpyPenguin anchors=(0,0..1,1) offsets=(±100) rect=(1280x1357)` ; après overscan `rect=(2484x2661)` ✓ (2,3×) ; **`masks: panel=HUD RectMask2D=False | ScreenCanvas RectMask2D=False`** (pas de clip).
  - **Le `Fader` n'est pas le fader visible** : code vanilla L299-313 — `fader.color.a = max(fadeAnim, clamp((playerDistance−95)/100, 0, 0,99))` → **invisible < 95 m** (toujours pendant le pilotage). L'overscan est correct mais invisible en test. Le « fader » de l'user = la **bordure noire du viseur** (`Border`, non overscan, taille canvas = 66° < 110°).
  - **Distance** : `StatusTexts[Title|Status|Mode|Connection]` est sur la TV (pas de texte distance visible au depth 4) — la distance est peut-être sous `Border`/un GO déplacé → **log `head-locked subtrees:`** (sous-arbres prof. 3 des 5 GO head-locked, one-shot) ajouté au build 11:59.
  - `SelfieCanvas` : parent d'origine = `uGUI(Clone)` (hors HUD), 2400×1350, point anchors + gros offsets.
  - `textGroup.SetActive(!selfieBorderManager.hasActiveBorder)` (L291) : les textes de statut sont masqués en mode selfie.
- **Option hauteur** (build 11:59) : `Settings.SpyPenguinHudHeightOffset` (cm, ±100, défaut 0) + slider « SpyPenguin HUD Height (cm) » ; driver : `h = panelEyeHeight + off/100`.
- **Build 12:07 — hauteur = caméra + HUD** (demande user : « l'option de hauteur doit modifier la hauteur de la caméra ET le point central du HUD ») : `CameraHeight_Patch` sur `SpyPenguin.LateUpdate` (private → patch par nom string) — après l'assignation ABSOLUE du jeu `SNCameraRoot.main.transform = cameraTransform` (L1046-1047), le postfix rajoute `rig.position += penguin.up * off` : auto-correctif, sans dérive, s'arrête seul (gate `ReturnUsingPenguin()` public + option). La vue suit `mainCamera.transform.parent` (`VRCameraRig.SetCameraTrackTarget` L829) → descendant du root du rig → l'offset se propage. Le centre du panneau = hauteur caméra+offset → **centré dans la vue**.
- **En attente de test** : `head-locked subtrees:` (où est la distance + structure de `Border` pour l'étendre au 110°), calibration hauteur, « trop bas ».

### Test 4 (11:1x) + fixes build 11:28
Retour user : « distance par rapport au joueur absente du HUD ; **fader trop petit, ne prend pas tout le champ de vision (≈110° en VR)** ; 1ʳᵉ ouverture OK, 2ᵉ « impression d'être au ras du sol » ; **option pour revenir au fonctionnement d'origine** ».
- **Log 11:10** : `filterHeadLocked=[Fader, Flash, Border, CameraUnavailable, SelfieCanvas]` ✓ (split OK) ; `eyeH=0,546` (la cam du robot est à 0,55 m au-dessus de la racine — le robot est petit) ; `camDist=1,1` ; `camFov=110`.
- **Option « SpyPenguin HUD on robot »** (default ON) dans l'onglet options, après HUD Offset Y : `Settings.SpyPenguinHudOnRobot` (bool public static → **persistance auto** via `SerializeModSettings`/réflexion, clé `SubmersedVR/SpyPenguinHudOnRobot`, protobuf bool tag 0x10). Gate dans `SetPenguin_Patch` + retry `LateUpdate` ; toggle off → `Detach()` (rendu `internal`). Pas de bootstrap nécessaire : le pilotage du robot survient bien après `GameSettings.LoadAsync`.
- **Fader/flash plein champ** : le canvas écran fait ~66° < les 110° de la vue VR → `FilterFullViewOverscan = 0,65` sur fader + flash (ancres −0,65..1,65, offsets 0 — trick overscan du loading screen) → ~2,3× le canvas ≈ 145°, couvre la vue. Border/camUnavailable/selfieBorder sans overscan.
- **2ᵉ ouverture « ras du sol »** : géométrie loguée à CHAQUE attach (pas plus qu'un one-shot) — les deux attaches 11:10 donnent la même eyeH=0,546 ; le prochain test donnera les positions root/cam/panel de chaque ouverture.
- **Distance absente** : tree one-shot du panneau (profondeur 3, APRÈS le split) → `SpyPenguin world HUD tree (TV):` pour voir ce qui reste sur la TV et où vit `textDistance` (peut-être sous un GO filtre déplacé).

### Test 3 (10:5x) + fixes build 11:10
Retour user : « aucune différence visible ; hauteur cam toujours au ras du sol ; 2× la 1ʳᵉ prise de control = aucun HUD (2ᵉ ouverture = OK) ; toujours trop grand ; **le fader est fix sur le robot et pas plein écran sur ma caméra** ».
- **Log : `filterHeadLocked=[]`** — les 5 éléments filtre ne sont PAS enfants directs du GO « HUD » (mon check `rt.parent == panel` les excluait tous) → restriction retirée (re-parentés depuis n'importe où dans la hiérarchie).
- **Le GO « HUD » = rect PORTRAIT 1080×1157 px** (1,2×1,285 m) : la TV est une dalle portrait → haut/bas coupés. `PanelScaleMultiplier` 1,0 → **0,75**.
- **Hauteur** : plus de valeur magique — `panelEyeHeight` calculé à l'attach = hauteur de `cameraTransform` au-dessus de la racine du robot (« au niveau des yeux »).
- **1ʳᵉ prise de control sans HUD** : le rect n'est pas résolu au moment du 1ᵉʳ `SetPenguin` (attach à 0×0, invisible) → retry `LateUpdate` : attach dès que le rect est résolu pendant qu'un penguin est actif ; + garde dans `Attach` (rect < 100 px → log warning, on retente).
- **Diagnostics one-shot** : `SpyPenguin world HUD diag:` (pos root/cam/panel, camDist, camFov via `SNCameraRoot.main.mainCam`) + `SpyPenguin world HUD hierarchy:` (chemins complets de fader/flash/border/camUnavailable/selfieBorder/content/exploreCam) — pour valider le split et caler taille/distance avec les vraies valeurs.

### Test 2 (10:4x) + fix build 10:5x
Retour user : « le HUD est complet mais **trop grand** (haut/bas pas visibles) ; caméra un peu **plus haute (+10 cm**, au niveau des yeux du robot) ; **le plan qui apporte le flou, les bordures noires etc (transparent pour la plupart) doit rester lié à la caméra** : tout le champ de vision, toujours en filtre sur le monde ; pas grave si le HUD est « derrière » ».
- `PanelScaleMultiplier` 1,3 → **1,0** ; `PanelHeight` 0,8 → **0,9** m.
- **Split filtre/HUD** : les enfants plein écran transparents du GO « HUD » sont re-parentés au ScreenCanvas (head-locked, filtre plein champ) à l'attach, et restaurés au panel au detach : `fader` (assombrissement distance), `cameraFlash`, `exploreBorder` (bordures noires), `cameraUnavailable`, `selfieBorderManager`. Le reste (feed `exploreCam`/`selfieCam`, `content` statut/sliders/pointeurs, aperçu photo, `connecting`) reste sur la TV du robot. Le bruit/flou caméra = post-effect `OnRenderImage` (`SpyPenguinCameraScreenFX`), déjà lié à la caméra par nature (non touché). Log `filterHeadLocked=[...]` à l'attach.
- Confirmation code BZ (SpyPenguin.LateUpdate 1040-1048) : pendant le pilotage (`isUsingPenguin`), `SNCameraRoot` est déplacé chaque frame sur `cameraTransform` → la vue du joueur = la vue du robot (justifie le design).

## Session 28/10 10:06 — Loading screen « figé dans le monde » : tenté, ROLLBACK
Retour user : « le canvas de loadingscreen, il faudrait de head locked : figer dans le world ». Tentative (build 10:06) : patch `LoadingScreen_WorldLock` (capture du world pos+rot de la racine à la 1ʳᵉ frame visible, puis pin chaque frame). **Retour user : « pas bon du tout, il n'est pas sur le bon axe »** → rollback (checkout de `VRLoadingScreen.cs` au commit `300517f`, comportement head-locked d'origine restauré).
- Cause probable (à creuser si on retente) : le pin world pos+rot sur une racine dont le parent (screen canvas) a un localScale/rotation non triviaux ne restitue pas l'orientation de plan attendue — l'écran sortait sur un mauvais axe. Une retente devrait plutôt se faire en espace local du screen canvas (contrôler le local pos/rot de la racine pour annuler la rotation de la tête, sans toucher au world) ou via un canvas dédié.

## Session 28/10 09:xx — unblock SeaTruck simplifié (bouton seul) + branche Seamoth morte retirée
Retour user : « il n'y a pas de seamoth dans BZ » (→ la branche `is SeaMoth` du 00:16 était morte : retirée, les 2 patches repassent `is Exosuit` seul ; le HUD statique reste corrigé pour prawn/SeaTruck) + « revoit et simplifie le unblock du seatruck : j'aime bien le bouton, mais dans ce cas retire la case à cocher ».

### Avant
- Case « SeaTruck Rescue (F9) » (`Settings.SeatruckRescueEnabled`, défaut **false**) qui gated : F9, Shift+F9, le **bouton options** (le bouton ne faisait rien si la case était décochée — mauvaise UX), et l'auto-rescue du watchdog.
- Sondes clavier temporaires (27/10) dans `Update()` : log `anyKeyDown` 1/s + log F8 (pour diagnostiquer pourquoi F9 n'arrivait pas en mode VR) → le bouton existe parce que le clavier n'atteint pas le jeu en VR.

### Après (build 28/10 09:13)
- **Case supprimée** (champ `SeatruckRescueEnabled` + `AddToggleOption`), les 4 gates retirées du watchdog.
- **Bouton conservé** (seul contrôle dans les options) : « SeaTruck: rescue now » → `RescueNow()`, toujours actif (label = const `SeaTruckWatchdog.RescueButtonLabel` — **sans mention des hotkeys** à la demande user 28/10).
- **F9 / Shift+F9 conservés** (plus de gate) : Shift+F9 (eject) = dernier recours car **dans certains cas les options ne sont plus accessibles** (confirmé user : il a déjà eu besoin de l'eject). Le clavier n'arrive pas en VR (sondes 27/10) mais ça couvre le desktop / les cas où le clavier passe.
- **Aucune auto-réparation** (retrait demandé 28/10) : plus de check post-load ni périodique (supprimés : `LoadCheckDelay`, `CheckInterval`, `playerReadyTime`, `lastIssueWasBad`, cooldown). Manuel uniquement (bouton / F9 / Shift+F9).
- **Analyse à l'ouverture des options** : `CreateOptionsTab.Postfix` (AddTabs = à chaque ouverture) appelle `SeaTruckWatchdog.RefreshRescueButton(panel)` → `EvaluateIssue()` **sans réparer** ; si pb détecté → le label du bouton passe en **rouge** + « SeaTruck: DOCK PROBLEM DETECTED - CLICK TO RESCUE (F9) » (le panneau étant reconstruit à chaque ouverture, pas besoin de reset). Coût : 1 `FindObjectsOfType<SeaTruckSegment>` + 1 `OverlapSphere` par ouverture = négligeable.
- Sondes clavier F8/anyKeyDown supprimées (diagnostic 27/10 terminé : le clavier n'arrive pas en VR → le bouton est la voie principale).

## Session 27/10 23:27 — reticle figé à -20 + tracking Static resserré au builder menu
Retour test 23:14 :
1. « le reticle est bien à -20 : tu peux figer la valeur en dur et retirer l'option »
2. « le menu des options marches, le PDA aussi »
3. « il y a d'autres caneva qui ont été affecté : revert la modif et cible un peu mieux » — le tracking Static LARGE (tous les scalers Static) a cassé le **screen de loading** et le **HUD** (et potentiellement d'autres).

### Build 23:27 (271872 B)
1. **Reticle figé** : `ReticleBillboard.AnchorOffset = (0, 0, 0.2)` (le « -20 cm » du slider, converti : `z = -(-20) × 0.01 = +0.2` le long de l'axe de l'outil). Supprimés : le champ `Settings.HandReticleHeightCm`, le slider « Hand reticle height (cm) », `ReticleBillboard.GetAnchorOffset()`, `VRHud.ApplyReticleHeight()`. Le write par frame et les 2 setups repassent à `AnchorOffset` direct.
2. **Tracking Static resserré** : `uGUI_CanvasScalerStaticTrack_Fixer` ne traite plus QUE les scalers dont le GO porte `uGUI_BuilderMenu` (gate `IsTrackedScaler` dans Prefix+Postfix, même pattern que le patch PDA avec `uGUI_PDA`). Tous les autres Static (HUD/ScreenCanvas, loading screen, options/IngameMenu, ScreenCanvas, etc.) reviennent au **comportement commité** : placement unique, figé dans le monde, parallaxe tête.
   - Justification du scope : le builder menu est le SEUL panneau ouvert en marchant (la motivation du tracking). Les menus in-game/options : jeu en pause → le placement unique reste devant, pas besoin de tracking (comportement commité validé).
    - Mécanisme inchangé pour le builder menu : capture de l'offset monde au placement, suivi translation par frame, rotation fixée (pas de head-lock).

## Session 28/10 17:06 — Cinématiques : table par cinématique + contrat + watchdog + recenter différé

Implémentation de la spec `docs/superpowers/specs/2026-10-28-player-cinematics-management-design.md` (relue : revue externe 28/10 + revue agent critique 28/10 — §12/§13 de la spec) via le plan `docs/superpowers/plans/2026-10-28-player-cinematics-management.md` (7 tâches, 1 déviation : `CineGoalListener` passé `public` — le nested default est `private`, inaccessible de `Cinematics.cs`).
- **Table de code** `Tweaks/CinematicsTable.cs` : modes `Play` (rotation copiée, défaut inconnues) / `PlayNoRot` / `Skip` + `Speed` (1/5/20/500). La liste des ~23 noms en dur est retirée de `Cinematics.cs` ; les 18 cinés jouées sans rotation avant sont pré-remplies `PlayNoRot` (comportement initial identique) ; user : passer chaque nom à `Play` au fil des tests. `!playInVr` conservé (playInVr=true jamais skip) ; toggle sleeper `CinematicsVR.hasSlept` inchangé ; `accel` sans director → fallback `Play x1` (log `accel->play`).
- **Contrat capturé** au START : clips anim (+events `AnimationClip.events`), EventTrack (`clip.displayName` + `eventName`), ControlTrack, bindings (warning `RISK-BINDING`), `timeUpdateMode`/`extrapolationMode`. Goals de story observés via `IStoryGoalListener` POCO (`[Cine] GOAL`/`GOAL-RESET`), retiré sur EndCinematicMode + sceneUnloaded. Verdict `[Cine] END ... nearEnd=...m goals=[...]` + `[Cine] SKIP-CANDIDATE?` à l'END.
- **Watchdog** (postfix `ManagedLateUpdate`) : `GAP-HITCH` (dt>0,1s), `GAP-DIR` (saut director.time > speed×dt×2+0,05 — corrélation contrat `GAP-CONTRACT`), `GAP-BACK` (wrap/seek), `RISK tunneling` (saut >1 m, Play ×1, state Update), `STUCK` (expireTime dépassé + Playing/null), accélération `SetSpeed` multi-racines par frame (self-healing), reset de vitesse en 3 couches (postfix `EndCinematicMode` + postfix `BreakCinematic` + filet watchdog AVANT le filtre d'activité), `WatchdogReset` sur `LogStart` + `sceneUnloaded`.
- **Recenter différé** SeaTruck + prawn : boucle 0,25 s, attend 1 s ET `cinematicModeCount==0`, gardes par type (SeaTruck : `__instance.piloting` — `currentMountedVehicle` NON posé par `StartPiloting` ; prawn : `currentMountedVehicle`), max 30 s (log `ABORTED`), host `CoroutineHost` (namespace `UWE` — using ajouté). Cyclops/hoverbike : recenter immédiat inchangé.
- **Inchangé (décision A user)** : le couplage de la case avec `cameraBobbing` et le fix head-look hoverbike ; tooltip mis à jour (« Checked = legacy ... Unchecked = per-cinematic rules »).
- **À tester (build déployé)** : (1) SeaTruck docké + non docké : `[Cine] START/END` + `[VR] delayed recenter (SeaTruck)` après la fin de la cinématique + orientation bonne après redressement de tête ; (2) prawn : idem ; (3) inconnue non tabulée : jouée + contrat complet ; (4) `hatch_side_anim` : `SKIP-DECISION ... table=True` ; (5) hitch volontaire : `GAP-*` ; (6) sortie véhicule avant 1 s : pas de recenter ; (7) boot/F2 non régressés ; (8) mort pendant cinématique : restore propre (`speed reset`, pas de STUCK parasite).
- **Phase 2 (data-driven, après lecture des logs)** : compensation events perdus (re-fire `StoryGoal.Execute` / `HandleTimelineEvent`), mute audio / sous-pas `Evaluate` si un cas d'accélération le demande, vue UI lecture seule.

### Test 1 (17:2x) — retour user + lecture des logs : OK
Retour user : recenter après entrée SeaTruck **OK** ; « pas mal d'autres animations » passées ; **pas de menu d'options pour changer le mode de skip** (normal — par design les règles sont dans le code `CinematicsTable.cs`, pas d'UI runtime ; seul toggle runtime = « Enable desktop cinematics » legacy) ; montée/descente d'échelle « instant, mais ça marche bien ».
- 12 START : `DockPlayerCinematic` ×2, `model` (échelle moonpool) ×2, `LaunchFromLeft`, `seatruck_anim` ×2, `hatch_end_anim` ×3, `seatruck_module_prawn_anim` ×2, `submarine_hatch_02_doorway_01_cin2` ×2.
- `[VR] delayed recenter (SeaTruck)` ×1 + `(Prawn)` ×2 ✓ (après la fin de la cinématique d'entrée, comme demandé).
- **Aucun** STUCK / GAP-DIR / GAP-CONTRACT / RISK-BINDING / speed reset. Un seul `GAP-HITCH dt=0,324` au START de `DockPlayerCinematic` (one-shot, frame de démarrage). Lignes `END state=Out` : nearEnd = 0,21–1,04 m (atterrissages propres). 1 NRE catchée dans `SetVrActiveParamFixer` (try/catch existant, receveur zombie — pas une régression).
- **Toutes ces cinématiques entrée/sortie ont `director=null`** (animator uniquement, pas de timeline) → la capture de contrat et l'accélération `SetSpeed` ne s'appliqueront qu'aux cinématiques à director (story : intro, Marge, etc. — non jouées cette session).
- Lignes `END`/`SKIP-CANDIDATE?` **doublées** = comportement jeu : le composant `OnPlayerCinematicModeEndForward` (décompilé) relaie un event d'animation unique vers le groupe de contrôleurs frères (dock = 8 variantes Launch/Dock/Exo/Truck, baies = clones) qui se terminent ensemble. Flood d'`END-MODE` en fin de session = teardown `OnDisable` de tous les contrôleurs démarrés. Cosmétique, pas d'action.
- `model` (échelle) : jouée 1,62–1,63 s, nearEnd 0,21 m — c'est la plus courte de la session (d'où l'impression d'instantanéité), rien à changer.

### Test 2 (17:4x) — retour user + corrections de table
Retour user : écoutille de la base = **OK** (skippée ✓) ; **bug wagon** : entrée dans un wagon (module prawn du seatruck) « tronquée, position finale pas top, pas pu sortir, état de nage comme si j'étais sorti mais coincé dedans » ; entrée/sortie prawn + seatruck et docking/undocking moonpool « toujours là » ; montée/descente du fox « a marché » (non ciblé) ; écoutille du bassin de confinement pas instantanée.
- **`hatch_end_anim`** : 4× `SKIP-DECISION table=True` + téléport propres ✓ (écoutille base = OK user).
- **`seatruck_module_prawn_anim`** : le skip a **cassé l'entrée véhicule** — téléport OK mais la machine à états du véhicule n'a pas reçu la confirmation d'entrée (normalement portée par la fin de l'animation) → état de nage + coincé + pas d'issue. **Repassé `Play`** (jouée 1,5–1,9 s) : les entrées VÉHICULE ne se skippent pas (contrairement aux hatches où il n'y a pas de machine à états véhicule).
- **`hatch_alienContainment_anims`** (écoutille bassin de confinement) : jouée 1,28–1,31 s, endTransform à 0,14–0,33 m → **ajouté `Skip`**.
- **`seatruck_anim`** (entrée/sortie poste de pilotage, 1,2–1,7 s) : GARDÉE jouée — l'auteur du mod d'origine l'avait aussi mise dans la liste « ne pas skipper » ; le bug prawn montre le risque sur les entrées liées à un état véhicule/pilotage.
- **Docking/undocking moonpool** (`DockPlayerCinematic` 5,9 s + `LaunchFromRight` 4,5 s) : toujours jouées — `playInVr=True` (garde du prefix : jamais skip) + receveur `moonpool_dock` (le skip historique cassait le HUD). Partie véhicule = tolérée user ; accélérer/instantanéiser = phase 2 (ré-émission du receveur). Les 8 entrées machinerie de la table n'ont pas tiré : ces contrôleurs ne STARTent jamais (finis par forwarding), et `LaunchFromLeft/Right` ont `playInVr=True`.
- **Fox (hoverbike)** : « a marché » car il n'y a **aucune cinématique** à l'entrée/sortie (aucun contrôleur START dans les logs) — entrée/sortie = simple changement d'état, instantané par nature. Rien à cibler.
- « Entrée/sortie prawn toujours là » : dans les logs, l'entrée prawn de la **base principale** ne démare AUCUNE cinématique (recenter `[VR] delayed recenter (Prawn)` sans START) — si l'utilisateur voit une animation sur ce point, à préciser (peut-être l'animation du prawn lui-même, hors PlayerCinematicController).
- Pas de STUCK/GAP-DIR/RISK ; 1 NRE d'un autre mod (`VisibleLockerInterior.OnSceneLoaded`, non lié) ; le NRE `SetVrActiveParamFixer` persiste (receveur `VehicleDockingBay` zombie pendant les cinématiques dock, catché — préexistant).

### Test 3 (18:1x) - Build 1 instrumentation + lecture des logs : PAS DE BLOCAGE

Build 1 smart-skip (commit faea73a, 311296 B) : log des signaux machine a etats (ANIM-PARAM x3 impls IAnimParamReceiver, VEHICLE-ENTER/INSIDE/EJECT, EXO-ENTERED), contrat animator (clips/events/params via reflexion), verdict END-STATE (mounted + exoFullyEntered), watchdog GAP-ANIM. Deviation plan obligatoire : le module engine `AnimatorModule` (AnimatorController & co) n'existe nulle part dans l'install (verifie par scanner de metadata + probe compilateur) - T1/T4 en reflexion runtime (log-only, zero risque comportement). `Animator.speed` confirme compilable par probe.

Retour user : pas de blocage sur les entrees/sorties a vitesse normale.

Lecture des logs (2922 lignes, 1 session) - decouvertes cles :
- **Le "Prawn" BZ = la classe `Exosuit`** (pas de classe Prawn/SeatruckModule dans le DLL - verifie IL ; le string "Prawn" n'existe que dans firstpass). Nos recenters "Prawn" patchent `Exosuit.OnPlayerEntered`.
- **Transfert module (truck->prawn, `seatruck_module_prawn_anim` x4 : enter 1,5s / exit 2,0s) : AUCUN signal** (pas de VEHICLE-ENTER/INSIDE/EXO-ENTERED, END-STATE mounted=null). Le joueur est deja en LockedPiloting du truck ; le transfert = re-parentage sur le siege prawn + teleport endTransform. L'anim est purement visuelle mais indispensable (deplace le joueur) - c'est pourquoi le skip cassait l'etat (test 2) : pas un signal manque, le joueur restait sur le mauvais siege.
- **Commit truck = `SeaTruckMotor.StartPiloting` au clic trappe** (`EnterLockedMode(pilotPosition, teleport:false)` + `inSeatruckPilotingChair`), avant l'anim et independant de celle-ci (`SeaTruckSegment.EnterHatch`/`OnClickHatch`). `SetPlayer`/`PropagatePlayer` propagent l'etat player + param animator `player_inside` sur toute la chaine de segments.
- Entrée directe exo au clic (x2) : `VEHICLE-ENTER teleport=True` + `EXO-ENTERED fullyEntered=True` + recenter Prawn OK. Depart prawn amarre occupe (`ExoDockPlayerCinematic` + `LaunchFromLeftExo`) : `VEHICLE-INSIDE True` + `EXO-ENTERED` + `VEHICLE-ENTER teleport=False` + END-STATE mounted=Exosuit exoFullyEntered=True.
- Dock/undock : `ANIM-PARAM moonpool_dock/launchright True->False` sur SeaTruck + DockBottom. Ecoutilles (submarine_hatch_02 front/back, containment, end_anim) : aucun signal, purement visuel. `model` (echelle) x2, `constructor_unequipped` x3 (NPC constructeur, pas le joueur).
- Aucune GAP-ANIM (forward), aucun STUCK ; GAP-ANIM-RESET normaux (changements d'etat) ; recenters SeaTruck x2 + Prawn x2 OK.

### Build 2 (18:45) - fast-play x5 prawn + truck (portee confirmee par user)

- **T5** (CinematicsDiagnostics) : statics `speedAppliedAnimator`/`speedAnimatorOwner`/`accelAnimLoggedName` ; `accel = rule.Speed > 1f` (tous modes - le truck est PlayNoRot) ; par frame dans la branche animator : `animator.speed = rule.Speed` + log one-shot `ACCEL-ANIM` ; resets miroir du bloc director (WatchdogReset + filet watchdog : anime detruit / owner gone / cine ended -> speed=1 + log).
- **T5b** (Cinematics) : retrait du fallback `accel->play (no director)`.
- **T6** (table) : `seatruck_module_prawn_anim` -> { Play, 5 } ; `seatruck_anim` -> { PlayNoRot, 5 }.
- Securite (validee par les logs) : les bools enter/exit sont persistants (pas de pulse < 1 frame) ; re-parentage + teleport endTransform = effets de fin de clip, intacts ; commits hors anim.
- **A tester (test 4)** : transferts prawn x2-3 + entree/sortie truck x2-3 a x5 : pas de blocage/nage, anim ~5x plus rapide, log ACCEL-ANIM + zero GAP-ANIM, recenters OK. Si propre -> montage x10 ; si bug -> x2.5 ou Play.

### Test 4 (18:5x) - x5 : BLOQUANT exit du wagon (9,6 s) + demande skip echelle

Retour user : (1) **bloqué dans le wagon, pas pu sortir** ; (2) le seatruck x5 est OK ; (3) « pas quand on est dans le moonpool » (à préciser) ; (4) l'échelle moonpool (`model`) doit être skipée.
**Clarification user (après analyse)** : le bug est à **l'exit du wagon** (le SeaTruck qui docke le prawn) - c'est-à-dire l'animation qui ramène le joueur du prawn **amarré** au poste du wagon = `seatruck_module_prawn_anim animParam=exit` (la seule séquence bloquée du log). Le libellé « exit du prawn » de la première analyse était faux.

Logs (13010 lignes, 1 session) - incident exit du wagon (lignes ~2279-2360) :
- ENTER (wagon->prawn amarré) x5 OK (82,19->82,69 : In->Out direct, END t=0,25s, speed reset net).
- EXIT du wagon (prawn amarré -> poste truck) x5 : START 84,41 (animParam=exit, interpolateAnimParam=exit_prep) -> ACCEL-ANIM speed=5 -> `GAP-ANIM-RESET t 4,22->0,87` (le clip enter **loopait** pendant que le joueur était assis : 2,75 tours) -> In->Update 84,66 -> **bloqué en Update 9,6 s sans aucune ligne END** -> user quitte le jeu (flood END-MODE + LargeWorldStreamer::Deinitialize).
- Les 6 cines `seatruck_anim` (truck) finissent toutes en t=0,35-0,39s : le truck se termine par **AnimationEvent en fin de clip** (t réel 0,36 < longueur du clip => pas le forced-end du jeu) - c'est pourquoi le truck passe à x5 sans blocage.
- `model` (échelle) x2, jouées 1,63-1,64s, endTransform OK (nearEnd 0,2m en Out).

**Cause racine (vérifiée dans le décompilé `PlayerCinematicController.ManagedLateUpdate`)** : le forced-end de l'état Update compare du **temps RÉEL** (`num = Time.time - timeStateChanged`) à la longueur du clip **actuel** (`animator.GetCurrentAnimatorStateInfo(0).length`), avec garde `enforceCinematicModeEnd && !animator.IsInTransition(0)`. À x5, la machine à état arrive au prochain état (clip **idle** long, ~9,6s) avant que le temps réel rattrape la longueur du clip exit : le forced-end attend donc la durée du clip idle en temps réel => 9,6 s d'input verrouillé. À 1x, les deux horloges sont synchrones => le forced-end tire exactement à la fin du clip. (`OnPlayerCinematicModeEnd()` est idempotent : garde `!cinematicModeActive || onCinematicModeEndCall` - c'est LA sortie conçue, appelée par les AnimationEvents via informGameObject.SendMessage.)

### Build 3 (19:13) - force-end à la fin du clip en temps ANIM (commit 4f8ec71, 313856 B)

- **T7** (CinematicsDiagnostics) : `TryGetAnimState` (reflexion : normalizedTime/length/**name**) remplace `GetAnimatorStateTime`. Force-end accéléré (un coup par cine, branche animator, `accel` seulement) : à l'entrée en `State.Update`, on **anchor** le clip courant après stabilisation de la machine à état (`IsInTransition(0)` faux + nom d'état stable 2 frames - attend la transition prep->real déclenchée par le flip `interpolateAnimParam=false` d'In->Update) ; on appelle `OnPlayerCinematicModeEnd()` quand le clip a joué sa longueur restante en temps ANIM (`deadline = anchor + (len - now)/speed + 0,15s`), ou si le clip est clamped à sa fin (même état, `animNow >= len - 0,05`), ou si la machine à état a quitté l'état anchor après que le clip a atteint sa fin (temps PRE-update), ou timeout 2s en Update (transition jamais stabilisée). Log `ACCEL-FORCE-END name=... clip=... reason=deadline|clamped|left|timeout`.
- **Fix re-arm** : le watchdog ne se re-armorait PAS sur une nouvelle cine du MÊME contrôleur (enter/exit prawn = même GO) - le `lastAnimTime` 4,22 stale du clip enter loopé polluait la détection. Re-arm si `activeWatch != __instance || startTime != lastArmedStart` (`startTime` bumpé par LogStart à chaque start).
- **Table** : `model` (échelle moonpool) -> { Skip } (demande user) - NOTE : nom de GO générique, si une autre cine utilise un GO nommé "model" elle est aussi skipée (seule l'échelle vue dans les logs 28/10).
- **A tester (test 5)** : exit du wagon (prawn amarré -> retour au poste truck, le bug) - doit se terminer en <~0,5s avec log `ACCEL-FORCE-END` + END + END-STATE (le joueur doit retrouver le poste de pilotage du wagon, pas rester coincé) ; enter (wagon->prawn) + entrées/sorties poste truck inchangés (l'événement de fin clip tire d'abord) ; échelle moonpool instantanée (skip) ; et préciser le cas « moonpool » (point 3) - les 6 cines seatruck_anim du log sont toutes propres (0,35-0,39s), il faut savoir quelle action exacte coince.

### Test 5 (19:2x) - REGRESSION : force-end Build 3 trop tôt (séquences multi-états)

Retour user : bloqué plusieurs fois : (1) en entrant dans le seatruck, pas au pilotage ; (2) en sortant du wagon puis en rentrant, mode à pied mais HORS du wagon -> tombé dans le décors.

Logs (2255 lignes, 1 session) : dock (985) -> undock (1479) -> **exit seatruck** `animParam=player_drive_out` (1552) -> enter prawn module (1734) -> **exit prawn module** (1815) -> hatches (1979+). Deux `ACCEL-FORCE-END` :
- L1564 `seatruck_anim clip=0,33s animNow=0,22 reason=deadline` : l'anchor a capturé le **PREMIER état** de la séquence (clip 0,33s) pas le clip réel (~1,5s) ; force-end 0,25s après In->Update = fin du 1er état ; END-STATE **mounted=null** ; le joueur est téléporté avant les états d'commit de la séquence.
- L1838 `seatruck_module_prawn_anim clip=0,25s animNow=1,68 reason=clamped` : l'anchor a capturé un clip **looping de 0,25s** (1,68s = 6,7 tours) - la condition `clamped` (`animNow >= len - 0,05`) est **toujours vraie sur un clip looping** (normalizedTime > 1) -> force-end immédiat 0,06s après In->Update.
- **Diagnostic : le modèle « anchor le clip courant à In->Update, fin à sa fin » est faux** : les séquences d'entrée/sortie sont MULTI-ÉTATS (1er état court, souvent looping, puis clip réel...) et l'événement de fin du jeu est sur le DERNIER clip. À 1x le forced-end du jeu (temps réel >= len) ne pose pas le problème ; à x5, mon force-end a préempté l'événement du jeu sur le 1er état.
- Note : `interpolateAnimParam` (exit_prep) n'existe que sur `seatruck_module_prawn_anim` (null sur seatruck_anim) ; l'enter prawn module reste In->Out direct (jamais Update) ; la transition vers le clip réel se fait À In->Update (GAP-ANIM-RESET t x->0,00 sur les 2 séquences).

### Build 4 (19:31) - force-end = STUCK-NET, plus un driver (commit bd4f2be, 313856 B)

- Retrait de toute la machinerie d'anchor (deadline/clamped/left/timeout, stabilité 2 frames, IsInTransition d'armement).
- Nouveau : dans `State.Update` (cine accélérée, branche animator), force-end **uniquement si la séquence est STUCK** :
  - `clip-end-stuck` : le clip est ASSIS sur sa fin depuis 0,3s (`|animNow - animLen| <= 0,05` ET `animNow < 1,5×animLen` [exclut les clips multi-tours] ET pas de crossfade active `IsInTransition(0)`), ou
  - `total` : l'état Update dure > 2,5s réel (filet global).
  - Log `ACCEL-STUCK-NET name=... updateT=... clip=... animNow=... reason=...`.
- **Pourquoi plus de préemption** : une séquence qui marche se termine par l'événement du jeu en ~0,36s réel (les 6 cines seatruck du test 4) << 0,3s/2,5s ; un clip qui se termine et dont l'état passe au clip suivant fait retomber `animNow` (reset du compteur 0,3s) ; un clip looping ne reste jamais « assis » sur sa fin (sauf 1-2 frames, < 0,3s).
- **Cas du bug test 4** (exit clip clamped à 1,97, machine à état en attente de transition) : atEnd vrai -> 0,3s -> force-end à ~0,7s après Update (au lieu de 9,6s).
- **A tester (test 6)** : exit/entry wagon (poste truck) x2-3 - doivent finir ~0,35-0,5s comme avant le force-end (pas d'ACCEL-STUCK-NET en cas de fin propre ; si ACCEL-STUCK-NET raison=clip-end-stuck sur une séquence qui « marchait » avant -> le 0,3s est trop court, monter à 0,5s) ; exit/enter prawn amarré x2-3 (ACCEL-STUCK-NET attendu sur l'exit, ~0,7-1s) ; pas de tombé dans le décors, mounted correct en END-STATE.

### Test 6 (19:4x) - truck OK 4/4, exit prawn module : stuck mode #2 (looping prep)

- **Truck 4/4 propres** : t=0,35-0,36s, **zéro ACCEL-STUCK-NET** -> le principe (séquence accélérée + filet) est validé sur les séquences à événement : le filet ne préempte jamais.
- Enter prawn module propre (In->Out direct). Pas de chute/corruption, session continue après chaque séquence, pas de NRE lié.
- **Exit prawn module (le bug d'origine) : `ACCEL-STUCK-NET updateT=2,53s clip=0,25s animNow=3,88 reason=total`** :
  1. **Nouveau mode de stuck** : la machine à état est restée **2,53s sur un clip de 0,25s en BOUCLE** (3,88s = 15,5 tours) — la transition vers le clip d'exit réel (1,97s) n'a **jamais tiré**. Ce n'est plus le « clip clamped » du test 4.
  2. **Vitesse effective ≈1x** (3,88s d'anim en 2,53s réel) malgré speed=5 appliqué par frame — hypothèse : le **multiplieur `AnimatorState.speed`** de l'état prep (ex. 0,2x -> 5x×0,2=1x), à confirmer par la topologie.
  3. L'exit a « fonctionné » (commit + téléport au poste, END-STATE mounted=null = normal pour cette cine comme en 1x) mais en 2,8s au lieu de ~1s.
- **Décision (user) : on arrête de deviner -> topologger en priorité, puis fix data-driven.**

### Build 5 (19:48) - topologger machine à état (commit 6d6cd64, 317952 B)

- **Diagnostic uniquement, zéro changement de comportement** : au premier contact de chaque contrôleur (sans director), dump complet une fois par GO (réinitialisé à chaque scène) : `params=[name(type)]`, par layer : `defaultState`, par état : `state speed= clip=name@len événements fn@t`, transitions `-> dest exitTime= isExit= mode= cond=[mode:param=thr] dur=` + `ANY->` (anyStateTransitions) + sous-machines (depth 2).
- Via réflexion sur `originalController` (AnimatorOverrideController -> base AnimatorController ; les types concrets sont absents du reference set compile). `AnimationEvent.target` n'existe PAS dans l'API runtime 2019.4 (erreur CS1061) - les événements sont loggés fn@t seulement (cible = GO de l'animator ou informGameObject via le contrôleur).
- Le STUCK-NET logge maintenant `nt=` (normalizedTime) et `animeSpeed=` (vitesse réelle de l'animator) pour traquer l'anomalie de vitesse.
- **A tester (test 7)** : rejouer l'exit prawn module amarré + 1 entrée/sortie truck ; récupérer les lignes `TOPOLOGY` du log (elles répondent : quel clip finit la séquence, quel événement fait le commit, **quelle condition porte la transition prep->exit** (param ? exit-time ?), le speed de chaque état).

### Test 7 (19:5x) - topo illisible (type opaque) + stuck mode #2 confirmé

- Topo Build 5 : `ctrl=RuntimeAnimatorController base=RuntimeAnimatorController params=[] layers=null` — le type runtime est littéralement la base abstraite `RuntimeAnimatorController` (pas `AnimatorController`, pas `AnimatorOverrideController`, pas d'`originalController`) → pas de `layers`/`parameters` accessibles → **topologie des états/transitions illisible par cette porte**.
- `animeSpeed=5,00` au tir du filet (la vitesse ETAIT appliquée) ; `nt=16,01` (16 tours du clip 0,25s) ; vitesse effective ≈1,1x (2,78s d'anim / 2,53s réel) → **multiplieur du STATE ≈0,22** (propriété `AnimatorControllerStateInfo.speed`).
- Exit = idem test 6 : stuck 2,53s sur le clip 0,25s looping, filet `reason=total`, puis commit + téléport OK, session continue. C'est le « soucis » user (« dans le wagon du prawn »).
- **`exit_prep`/`enter_prep` n'existent PAS dans le code du jeu** (strings de prefab ; le seul SetBool est le setter `animState` du contrôleur). Pas de `SetTrigger` dans le code Seatruck/Exosuit sauf `seatruck_exit` (trigger du truck, pas du prawn module).
- Hypothèse en tête : transition prep->exit pilotée par un **trigger posé par code avec délai temps-réel** (la fenêtre de transition est passée à x5) — un param TRIGGER dans la liste `GetParameter` le confirmerait.

### Build 6 (20:01) - diagnostics par état (commit c08e0ba, 319488 B)

- `TryGetAnimState` retourne aussi `stateSpeed` (multiplieur du state) + `loopTime`.
- **`ANIM-STATE`** : log (throttlé 0,5s) pendant Update d'une cine accélérée + à chaque GAP-ANIM : `state= stateSpeed= loop= clip= animNow=`.
- **TOPOLOGY enrichi** : `baseType=` + **liste de toutes les props lisibles du type contrôleur** (pour trouver la vraie surface) + **params via `Animator.parameterCount`/`GetParameter(i)`** (API niveau animator, fonctionne même avec contrôleur opaque - révélera les params TRIGGER).
- `STUCK-NET` : ligne enrichie `state= stateSpeed= loop=`.
- **A tester (test 8)** : rejouer l'exit prawn amarré (le stuck) + 1 entrée/sortie truck ; les lignes `ANIM-STATE`/`TOPOLOGY params=[...]` du log donneront : le nom de l'état où la machine reste, son speed, son loop, et la liste des paramètres (bools/triggers).

### Test 8 (20:1x) - speed=5 confirmé mais clip avance à ~1x ; état sans nom ; params révélés

- User a parcouru la plupart des animes. Prawn module exit : même stuck (net `total` à 2,53s, commit + téléport OK ensuite).
- **TOPOLOGY params (prawn module)** : `speed(Float), vr_active, exosuit_docked, vehicle_nearby, exosuit_enter(_prep), screenUp, piloting, enter(_prep), exit(_prep), player_nearby, player_inside` — **AUCUN trigger** (hypothèse trigger morte). `ctrlProps=[animationClips,name,hideFlags]` → `animationClips` (AnimationClip[]) = seule surface lisible.
- **Truck** : params `player_*_in/out/get_up/sit_down/pull_lever(+_prep)`, `moonpool_*`, `bumpers`, attaques créatures, **`seatruck_exit(Trigger)`** (le trigger du truck, posé par SeaTruckMotor).
- **ANIM-STATE (stuck prawn)** : `state=` VIDE, `stateSpeed=1,00` (l'hypothèse 0,22x morte), `loop=False`, `clip=0,25`, animNow 1,19→3,94 → **vitesse effective ≈1,0x malgré animeSpeed=5,00 et stateSpeed=1,00** ; `nt=15,76` (clip « joué » 3,94s d'anim-time) avec loop=False = incohérent → transition perpétuelle ?
- Séquence jeu relue (PlayerCinematicController L645-682) : In = lerp player vers animatedTransform sur `interpolationTime`=0,25s (temps réel) ; à num2==1 : `state=Update` + `animState=true` (SetBool exit) + SetBool(exit_prep,false) — **exit=true et exit_prep=false sur la même frame, à t+0,25s réel**.
- **Pas de code jeu posant `speed(Float)`** sur cet animator (les SetFloat "speed" du code sont créatures/véhicules).
- Mystères ouverts : (1) pourquoi la transition prep→exit ne tire pas (tous les conditions sont des bools) ; (2) pourquoi ~1x (multiplieur fantôme = param `speed` lié au speed du state avec défaut ~0,2 ?) ; (3) nom d'état vide ; (4) `nt>1` avec loop=False.

### Build 7 (22:40) - diagnostic final (commit 4662f90, 321536 B)

- Nom d'état : essaie `fullName` puis `shortName` puis `name` (test 8 : `name` vide).
- `ANIM-STATE` : `inTrans=` (`animator.IsInTransition(0)`) + `dest=` + `transDur=` (GetCurrentAnimatorTransitionInfo) — une transition perpétuelle expliquerait à la fois l'anomalie 1x et la fin forcée du jeu morte (`if (IsInTransition) break`).
- `ANIM-PARAMS` : snapshot de **toutes les valeurs de paramètres** (GetBool/GetFloat/GetInt) à chaque échantillon ANIM-STATE + `STUCK-PARAMS` au tir du filet → lequel des bools attend la transition ; valeur du float `speed`.
- `TOPOLOGY-CLIPS` : dump `animationClips` (nom, longueur, AnimationEvents) → quel clip porte l'événement de commit, ce qu'est le clip 0,25s.
- **A tester (test 9)** : rejouer l'exit prawn amarré (le stuck) + 1 entrée/sortie truck. Le log doit identifier l'état bloqué + la condition manquante + l'origine du 1x → fix data-driven (build 8).

### Challenge externe (agent adversarial, 28/10 22:5x) — verdict : proceed test 7b, pivot du fix

L'agent a relu le code du jeu décompilé, le log test 8 et le code du mod. Corrections validées après vérification croisée :
- **H2 morte** (float `speed` lié au speed du state) : Unity n'a pas de binding param→speed de state ; et le TRUCK (qui marche) montre le même taux de temps-état rapporté ~1x avec `stateSpeed=0,01` (log 1653), et `clip=0,04 animNow=7,28` (log 1570) → **la télémétrie `animNow`/`nt` est intrustable sur ce build** ; seuls les changements d'état (GAP-ANIM-RESET) et l'absence de END sont des preuves dures. Mon modèle « 0,22x / 15,5 tours » était une mauvaise lecture.
- **Fast-path du net MORT sur le stuck mode réel** : `atEnd` = `|animNow-animLen|<=0.05 && animNow<1.5*len` soutenu 0,3s (CinematicsDiagnostics ~L1464) ; avec un animNow non borné, la fenêtre [0,20;0,375] est traversée en 0,175s < 0,3s → seul le backstop 2,5s tire. **Le lock 2,5s est structurel**, pas accidentel.
- **`enforceCinematicModeEnd`** (champ public, PlayerCinematicController L85, gate L689) **jamais lu par le mod** → prémisse non vérifiée (si false, le jeu ne peut JAMAIS auto-commit cette cine).
- **endTransform=NULL sur le truck** (log test 8, lignes END `seatruck_anim`) : le commit du truck vient de l'AnimationEvent du clip, pas de l'endTransform → explique la corruption du force-end build 3 (player laissé au pose intermédiaire, mode à pied).
- **`Animator.Play` = branche morte** : nom d'état illisible même sur le truck qui marche (artifact du contrôleur strippé) → impossible de cibler un état qu'on ne peut pas nommer.
- **« garantir la condition » = last resort** : écrire des bools du jeu = la classe de bug du test 2.
- H1' (transition perpétuelle / latch IsInTransition) reste la tête de liste pour expliquer l'absence de END ; la cause exacte restera peut-être inobservable.
- **Pivot validé par user (question)** : relâche précoce table-gated pour le prawn module (~0,7-1s) ; baseline 1x en follow-up (build 8b).

### Build 7b (22:58) — diagnostics challenge (commit d2650ee, 321536 B)

- `enforce=` loggé dans START + STUCK-NET ; `stateSpeedMult=` (speedMultiplier) en plus de stateSpeed dans ANIM-STATE/STUCK-NET.

### Build 8 (23:01) — relâche précoce prawn module (commits 7604cbd, 323072 B)

- Nouveau champ `CineRule.EarlyRelease` ; `seatruck_module_prawn_anim` = `{Play, 5, EarlyRelease=true}`.
- Watchdog : si `rule.EarlyRelease && inUpdate && !forceEndDone && t(Update)>0,5s` → `OnPlayerCinematicModeEnd()` (total ~0,75s depuis In, vs ~2,8s avec le filet). Guard runtime `endTransform != null` (précondition prouvée, non inférée) ; si absent : warning + filet 2,5s reste armé (forceEndDone non posé).
- Si la séquence complète naturellement avant le deadline, l'état quitte Update → la relâche ne tire jamais.
- Filet 2,5s conservé comme backstop (toutes cines) ; truck inchangé (0,35s).
- **A tester (test 9)** : exit prawn amarré (doit sortir en ~0,8-1s, `EARLY-RELEASE` dans le log) + 1 entrée/sortie truck (inchangé). Le log capture aussi `enforce=`, `inTrans/dest/transDur`, `TOPOLOGY-CLIPS`.

### Build 8b (à préparer après test 9) — baseline 1x

- Table : `seatruck_module_prawn_anim` → Speed=1f (pas d'accélération) ; sampling ANIM-STATE/ANIM-PARAMS non-gaté pour cines à 1x (throttle 0,5s) → capter la timeline exacte des bools du jeu quand la transition prep→exit tire à 1x (recherche fix racine, optionnel).

### Test 9 (23:0x) — BUG MAJEUR user : entrée prawn cassée + desync exit

- User : « gros bug en essayant de rerentrer dans le prawn amarré : je suis resté dehors mais en mode à pied => je tombe au fond de l'eau ».
- **`enforce=False` sur toutes les cines** (log START) → le jeu ne peut JAMAIS auto-commit ; le commit passe par AnimationEvent (ou rien).
- **TOPOLOGY-CLIPS enfin lisible** (prawn module) : `grab_in(1,04); grab_idle(0,21); push_idle(0,21); grab_out(1,04); get in(1,25)[OnPlayerCinematicModeEnd@1,24]; get out(1,67)[OnPlayerCinematicModeEnd@1,65]; screenFold/Unfold[OnAnimationSound@0,00]; unfoldedScreen(0,21); foldedScreen(0,21); proximityFold/Unfold(0,83); armsGrab(2,29); armsGrab180(2,92); armsGrabbed(0,42); armsRelease(2,29); armsResting(0,42); enterPrawn(1,25); armsFolded(0,21)` — **le clip d'entrée `get in` (1,25s) PORTE l'événement de commit** ; `get out` (1,67s) aussi.
- **ENTRÉE (t=54,44) cassée** : `None->In` → `In->Out` en 0,25s **SANS Update visible** ; END `nearEnd=0,92m` (player à 0,92m du siège), `mounted=null`, `movedFromStart=3,62m` → le commit a tiré à la fin de In, **avant** que le clip `get in` ne se joue → le player n'a PAS été monté dans le module → **dehors, mode à pied** (le bug user). AUCUNE ligne EARLY-RELEASE/STUCK-NET sur l'entrée → le mod n'a pas commité ; seul le chemin AnimationEvent `get in` reste (enforce=False, informGO=null, director=null) → la machine était déjà sur/finie du clip `get in` avant la fin de In (état antérieur inconnu — c'est ce que va donner ANIM-PRESTART).
- **EXIT (t=57,55)** : stuck comme tests 6/8 (0,25s clip, inTrans=False, params `exit=1 exit_prep=0 enter=0 enter_prep=1 player_inside=0`) → **EARLY-RELEASE à updateT=0,53s** (conçu) → END t=0,78s, nearEnd=2,27m → puis **`VEHICLE-ENTER name=Exosuit(Clone) teleport=True playAnim=True` + `Vehicle.OnPilotModeBegin` + `EXO-ENTERED fullyEntered=True` + `VEHICLE-INSIDE inside=False` + `VEHICLE-EJECT`** → le code véhicule du jeu se désynchronise : la relâche à 0,53s est TROP TÔTE (le 2,5s des tests 6/8 était sûr, le 0,53s non — la logique temps-réel du module a besoin de la séquence complète).
- **Conclusion** : l'accélération x5 casse l'ENTRÉE (commit précoce, non monté) et la relâche précoce désynchronise l'EXIT → **revenir à 1x pour le module** (comportement natif : entrée ~1,5s, sortie ~3s, prouvé) ; le x5 du module reste un objectif à étudier avec la timeline 1x.

### Build 8c (23:11) — retour 1x module + diagnostic phase In (commit b23ef61, 323072 B)

- Table : `seatruck_module_prawn_anim` → `Speed=1f` (EarlyRelease retiré de l'entrée en vigueur ; le mécanisme EarlyRelease reste dans le code, inactif).
- Sampling ANIM-STATE/ANIM-PARAMS : **phase In incluse** (avant : Update only, accel only) ; toutes cines, throttle 0,5s.
- **`ANIM-PRESTART`** : échantillon de l'état machine AVANT la phase In (le clip sur lequel la machine est assise au départ — la clé du commit précoce de l'entrée).
- **A tester (test 10)** : (1) entrer dans le prawn amarré (doit marcher en 1x, ~1,5s, player monté), (2) sortir (~3s), (3) si possible une entrée/sortie de plus. Le log donnera : PRESTART + timeline In/Update + params → la machine à 1x référence pour concevoir le x5.

### Test 10 (1x, build 8c) — baseline propre : 8 incidents module, tous OK

- 8 cinés module (4 entrées ~1,5s, 4 sorties ~1,93s), aucun bug user. **Timeline 1x référence :**
  - PRESTART : idle = boucle clip 0,21s (animNow accumule : 20s, 180s, 433s… — la télémétrie accumule entre les tours, cohérent).
  - **À la fin du set du bool `_prep` (début de In), la machine va DIRECTEMENT au clip de commit** : entrée → `get in` (1,25s) depuis 0,00 (`enter_prep=1 enter=0 player_inside=1`) ; sortie → `get out` (1,67s) depuis 0,00 (`exit_prep=1 exit=0`). **Pas d'état prep 0,25s à 1x** (le clip 0,25s des tests 5x = autre chose, cf. contamination).
  - In = 0,25s réel (lerp player) ; à In→Update : flip `enter/exit=1`. Commit = AnimationEvent à 99,2 % du clip (t≈1,5s / 1,93s) → propre.
- **Racine du bug entrée 5x (prouvée)** : événement à 1,24s d'anim-time = **0,248s à 5x < durée de In (0,25s)** → l'événement tire **2ms AVANT** le flip In→Update → commit pendant In → desync du montage (test 9). Le design du jeu (In=0,25s) supposait du 1x.
- **Correction d'interprétation** : la chaîne `VEHICLE-ENTER Exosuit teleport=True` + `EXO-ENTERED` + `VEHICLE-INSIDE inside=False` + `VEHICLE-EJECT` après la sortie du module est **du flux jeu normal** (présente après CHAQUE sortie propre à 1x, 6× dans le log) — l'interprétation « desync » du test 9 était fausse ; le vrai bug test 9 = uniquement l'entrée. (`nearEnd=0,86-0,92m` + `mounted=null` à l'END des entrées = normal aussi, identique en 1x.)
- Stuck 5x de la sortie (clip 0,25s, tests 6/8/9) : à 1x la sortie va direct à `get out` → le clip 0,25s n'est PAS l'état prep ; probablement lié à la contamination (test 9 : `enter_prep=1` résiduel de l'entrée cassée) ou dépendant de la vitesse. À re-vérifier en 5x propre.

### Build 9 (00:00) — vitesse par phase : In@1x + Update@5x (commit 5020d6a, 323072 B)

- Principe : **l'événement de commit ne doit jamais tirer avant le flip In→Update.**
- Nouveau champ `CineRule.InSpeed` (0 = même que Speed) ; module = `{Play, Speed=5, InSpeed=1}`.
- Watchdog : speed appliqué = InSpeed pendant `state==In`, sinon Speed (per-frame, self-healing).
- Entrée 5x : le clip joue 0,25s (20 %) à 1x pendant In → événement impossible dans In ; flip à 0,25s ; commit 5x à ~0,45s de In.
- Sortie 5x (état propre) : même mécanique → commit à ~0,53s de In (si la machine va à `get out` comme à 1x).
- Filet 2,5s conservé (backstop) ; EarlyRelease inactif (non prouvé en 5x propre).
- **A tester (test 11)** : (1) entrée prawn amarré — doit monter le player en ~0,5-0,8s total ; (2) sortie — ~0,5-0,8s ; (3) un cycle de plus si possible. Le log doit montrer : ANIM-STATE clip=1,25/1,67 pendant In ET Update (pas de clip 0,25s), ACCEL-ANIM `speed=5 in=1`, pas de STUCK-NET.

### Test 11 (build 9) — SUCCÈS : entrée 0,58s + sortie 0,61s, zéro stuck, user « pas vu de soucis »

- **Entrée (t=25,45)** : `ACCEL-ANIM speed=5 in=1` ; In : clip `get in` (1,25s) depuis 0 à 1x ; flip `enter=1` à In→Update (25,73) ; **commit à t=0,58s** (prévu ~0,45-0,6s), `nearEnd=0,23m` (mieux que le 0,91m du 1x), `mounted=null` (normal pour cette cine, cf. test 10) ; Out→None à 26,31. Pas de STUCK-NET, pas de EARLY-RELEASE.
- **Sortie (t=27,56, 1,2s après)** : état propre (`exit_prep=1 enter=0`, **aucune contamination**) ; In : clip `get out` (1,67s) depuis 0 à 1x ; flip `exit=1` ; **commit à t=0,61s** ; Out→None à 28,45. **Le stuck du clip 0,25s (tests 6/8/9) ne s'est PAS reproduit en état propre** → il était bien lié à la contamination (résidu `enter_prep=1` de l'entrée cassée) et/ou à l'anomalie 5x-pendant-In.
- User a continué la session (zone sous-marin, hatches, glow whale) sans incident ; aucun error de jeu.
- Bruit de log à noter (pas un bug) : `END-MODE name=glow_whale` en boucle (cine ambiante directeur en arrière-plan) — candidat à filtrer plus tard.
- **Statut fonction smart-skip** : truck 5x (0,35s) ✓, module prawn In@1x+Update@5x (~0,6s) ✓, hatches/échelle/dock skip ✓. Reste : le cas **moonpool** (jamais précisé par user) et le polish (bruit de log, éventuelle montée x10 du truck).
