using System.Collections.Generic;
using System.Threading.Tasks;
using Photon.Pun;
using UnityEngine;

public partial class OnlineSyncManager
{
    // チェーン中に手札から出した作戦。作戦ゾーンに着く前の発動アニメで使う。
    CardController chainTacticsInFlight;

    /// <summary>
    /// カード召喚のRPC同期
    /// </summary>
    public void SyncSummonCard(string cardKindStr, int cardID, int handIndex, string targetZoneName, SummonSource summonSource, bool specialSummon = false)
    {
        SendRPC("ReceiveSummonCard", cardKindStr, cardID, handIndex, targetZoneName, (int)summonSource, specialSummon);
    }

    /// <summary>
    /// カード召喚のRPC受信
    /// </summary>
    [PunRPC]
    private void ReceiveSummonCard(string cardKindStr, int cardID, int handIndex, string targetZoneName, int summonSourceInt, bool specialSummon)
    {
        PlayOpponentCardFromHand(cardKindStr, cardID, handIndex, targetZoneName, (SummonSource)summonSourceInt, specialSummon: specialSummon);
    }

    /// <summary>
    /// チェーン中に手札から出した作戦を、相手の作戦ゾーンへ出す。カード使用時の窓は開かない。
    /// </summary>
    public void SyncAppearTacticsDuringChain(string cardKindStr, int cardID, int handIndex)
    {
        SendRPC("ReceiveAppearTacticsDuringChain", cardKindStr, cardID, handIndex, "ETacticsZone");
    }

    [PunRPC]
    private void ReceiveAppearTacticsDuringChain(string cardKindStr, int cardID, int handIndex, string targetZoneName)
    {
        PlayOpponentCardFromHand(cardKindStr, cardID, handIndex, targetZoneName, SummonSource.手札, openUseChain: false);
    }

    /// <summary>
    /// 作戦ゾーンへ移動中で、まだ住所が決まらないカード。
    /// </summary>
    CardController FindChainTacticsInFlight(string cardKindStr, int cardID)
    {
        CardController card = chainTacticsInFlight;
        if (card == null)
            return null;
        if (!System.Enum.TryParse(cardKindStr, out CardKind kind))
            return null;
        if (card.cardKind != kind || card.cardID != cardID)
            return null;
        return card;
    }

    void ForgetChainTactics(CardController card)
    {
        if (chainTacticsInFlight == card)
            chainTacticsInFlight = null;
    }

    /// <summary>
    /// 相手のカードを、手札や墓地の表示から外してから指定ゾーンへ動かす。
    /// openUseChain が false のあいだは、着地前でも発動アニメの対象に残す。
    /// </summary>
    private async Task PlayOpponentSummon(CardController card, Transform targetZone, SummonSource summonSource, int sourceIndex, bool openUseChain, bool specialSummon = false)
    {
        bool trackUntilArrival = !openUseChain && card != null;
        if (trackUntilArrival)
            chainTacticsInFlight = card;

        try
        {
            if (card == null || card.transform == null)
            {
                Debug.LogError("PlayOpponentSummon: カードオブジェクトが無効です");
                return;
            }

            RectTransform fromZone = GetSummonSourceZone(summonSource);
            if (fromZone != null)
            {
                // 手札と墓地は、表示を組み直す前にキャンバスへ退避する。
                if (summonSource == SummonSource.手札 || summonSource == SummonSource.デッキ)
                {
                    Vector3 startPos = fromZone.position;
                    var canvas = BattleManager.Instance.mainCanvas != null ? BattleManager.Instance.mainCanvas.transform : null;
                    if (canvas != null)
                        card.transform.SetParent(canvas, false);

                    if (summonSource == SummonSource.手札)
                    {
                        RemoveCardFromOpponentHand(card.cardKind, card.cardID, sourceIndex);
                        BattleManager.Instance.UpdateEnemyHandUI();
                    }
                    else
                    {
                        RemoveCardFromOpponentGraveyard(card.cardKind, card.cardID, sourceIndex);
                        BattleManager.Instance.UpdateEnemyGraveyardUI();
                    }

                    card.transform.position = startPos;
                }
                else
                {
                    card.transform.position = fromZone.position;
                }
            }

            await BattleManager.Instance.SummonRoutine(card, targetZone, true, summonSource, openUseChain, specialSummon);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"PlayOpponentSummonでエラー: {ex.Message}");
            try
            {
                BattleManager.Instance.UpdateEnemyHandUI();
                BattleManager.Instance.UpdateEnemyGraveyardUI();
            }
            catch (System.Exception uiEx)
            {
                Debug.LogError($"UI更新でもエラー: {uiEx.Message}");
            }
        }
        finally
        {
            bool arrived = card != null && card.transform != null && card.transform.parent == targetZone;
            if (trackUntilArrival && (arrived || card == null))
                ForgetChainTactics(card);
        }
    }

    /// <summary>
    /// 相手の手札由来カードを生成し、指定ゾーンへ出す。
    /// </summary>
    private void PlayOpponentCardFromHand(string cardKindStr, int cardID, int handIndex, string targetZoneName, SummonSource summonSource, bool openUseChain = true, bool specialSummon = false)
    {
        if (!IsOnlineMode || BattleManager.Instance == null)
            return;

        try
        {
            CardKind cardKind = System.Enum.Parse<CardKind>(cardKindStr);
            CardController card = BattleManager.Instance.CreateCard(cardKind, cardID, BattleManager.Instance.eHandZone);
            if (card == null)
            {
                Debug.LogError($"相手カードの作成に失敗しました: {cardKindStr} ID:{cardID}");
                return;
            }

            Transform targetZone = BattleManager.Instance.GetZoneByName(targetZoneName);
            if (targetZone == null)
            {
                Debug.LogError($"ターゲットゾーンが見つかりません: {targetZoneName}");
                return;
            }

            _ = PlayOpponentSummon(card, targetZone, summonSource, handIndex, openUseChain, specialSummon);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"相手カードの登場でエラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 技使用のRPC同期
    /// </summary>
    public void SyncUseMove(CardController attacker, int moveIndex, bool moveHit, int extraHits)
    {
        if (!CardAddress.TryFrom(attacker, out CardAddress address))
            return;

        SendRPC("ReceiveUseMove", address.Kind.ToString(), address.Id, address.ZoneName, address.Index, moveIndex, moveHit, extraHits);
    }

    /// <summary>
    /// 技使用のRPC受信
    /// </summary>
    [PunRPC]
    private async void ReceiveUseMove(string cardKindStr, int cardID, string zoneName, int zoneIndex, int moveIndex, bool moveHit, int extraHits)
    {
        CardController attacker = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex);
        if (attacker == null)
            return;

        // カードの初期化が完全に終わるまで少し待機
        await Task.Yield();

        // modelがnullでないことを確認
        if (attacker.model == null)
        {
            Debug.LogError($"[ReceiveUseMove] 攻撃者カードのmodelがnullです: {attacker.cardName}");
            return;
        }

        BattleManager.Instance.SetPendingMoveHit(moveHit, extraHits);
        await BattleManager.Instance.UseMoveRoutine(attacker, moveIndex);
    }

    /// <summary>
    /// ターゲット選択のRPC同期
    /// </summary>
    public void SyncSelectEnemyTarget(CardController attacker, CardController target, int moveIndex)
    {
        if (!CardAddress.TryFrom(attacker, out CardAddress attackerAddress))
            return;
        if (!CardAddress.TryFrom(target, out CardAddress targetAddress))
            return;

        SendRPC(
            "ReceiveSelectEnemyTarget",
            attackerAddress.Kind.ToString(),
            attackerAddress.Id,
            attackerAddress.ZoneName,
            attackerAddress.Index,
            targetAddress.Kind.ToString(),
            targetAddress.Id,
            targetAddress.ZoneName,
            targetAddress.Index,
            moveIndex);
    }

    /// <summary>
    /// ターゲット選択のRPC受信
    /// </summary>
    [PunRPC]
    private async void ReceiveSelectEnemyTarget(
        string attackerKind, int attackerId, string attackerZone, int attackerIndex,
        string targetKind, int targetId, string targetZone, int targetIndex,
        int moveIndex)
    {
        CardController attacker = CardAddress.Resolve(attackerKind, attackerId, attackerZone, attackerIndex);
        CardController target = CardAddress.Resolve(targetKind, targetId, targetZone, targetIndex);
        if (attacker == null || target == null)
            return;

        // カードの初期化が完全に終わるまで少し待機
        await Task.Yield();

        // modelがnullでないことを確認
        if (attacker.model == null)
        {
            Debug.LogError($"[ReceiveSelectEnemyTarget] 攻撃者カードのmodelがnullです: {attacker.cardName}");
            return;
        }

        BattleManager.Instance.selectedAttacker = attacker;
        BattleManager.Instance.selectedMoveIndex = moveIndex;
        await BattleManager.Instance.SelectTargetRoutine(target);
    }

    /// <summary>
    /// 複数体を選ぶ技の対象を相手へ伝える。
    /// </summary>
    public void SyncSelectMoveTargets(CardController attacker, IReadOnlyList<CardController> targets, int moveIndex)
    {
        if (!CardAddress.TryFrom(attacker, out CardAddress attackerAddress))
            return;

        var kinds = new List<string>();
        var ids = new List<int>();
        var zones = new List<string>();
        var indices = new List<int>();
        if (targets != null)
        {
            foreach (var card in targets)
                CardAddress.TryAdd(card, kinds, ids, zones, indices);
        }

        SendRPC(
            "ReceiveSelectMoveTargets",
            attackerAddress.Kind.ToString(),
            attackerAddress.Id,
            attackerAddress.ZoneName,
            attackerAddress.Index,
            moveIndex,
            kinds.ToArray(),
            ids.ToArray(),
            zones.ToArray(),
            indices.ToArray());
    }

    /// <summary>
    /// 複数体を選ぶ技の対象を受け取り、選ばれたバトラー全員へ解決する。
    /// </summary>
    [PunRPC]
    private async void ReceiveSelectMoveTargets(
        string attackerKind, int attackerId, string attackerZone, int attackerIndex,
        int moveIndex,
        string[] targetKinds, int[] targetIds, string[] targetZones, int[] targetIndices)
    {
        CardController attacker = CardAddress.Resolve(attackerKind, attackerId, attackerZone, attackerIndex);
        if (attacker == null || attacker.model == null || BattleManager.Instance == null)
            return;

        var targets = new List<CardController>();
        int count = CardAddress.Count(targetKinds, targetIds, targetZones, targetIndices);
        for (int i = 0; i < count; i++)
        {
            CardController card = CardAddress.ResolveAt(targetKinds, targetIds, targetZones, targetIndices, i);
            if (card != null && !targets.Contains(card))
                targets.Add(card);
        }

        BattleManager.Instance.selectedAttacker = attacker;
        BattleManager.Instance.selectedMoveIndex = moveIndex;
        BattleManager.Instance.PrepareRemoteMoveResolution();
        await BattleManager.Instance.ResolveSelectedMoveTargets(attacker, moveIndex, targets, payEnergy: true);
    }

    /// <summary>
    /// 技の対象選択を、技を選ぶ前まで戻したことを相手へ伝える。
    /// </summary>
    public void SyncCancelMoveTarget(CardController attacker)
    {
        if (!CardAddress.TryFrom(attacker, out CardAddress address))
            return;

        SendRPC("ReceiveCancelMoveTarget", address.Kind.ToString(), address.Id, address.ZoneName, address.Index);
    }

    /// <summary>
    /// 相手が対象選択を取り消した。まだ対象が決まっていなければハイライトを消す。
    /// </summary>
    [PunRPC]
    private void ReceiveCancelMoveTarget(string cardKindStr, int cardID, string zoneName, int zoneIndex)
    {
        CardController attacker = CardAddress.Resolve(cardKindStr, cardID, zoneName, zoneIndex);
        if (attacker == null || BattleManager.Instance == null)
            return;

        BattleManager.Instance.AbortRemoteMoveTargetChoice(attacker);
    }

    /// <summary>
    /// 召喚元に応じたゾーンを取得するヘルパーメソッド
    /// </summary>
    private RectTransform GetSummonSourceZone(SummonSource summonSource)
    {
        switch (summonSource)
        {
            case SummonSource.手札:
                return BattleManager.Instance.eHandZone.GetComponent<RectTransform>();
            case SummonSource.墓地:
                return BattleManager.Instance.eDeckZone.GetComponent<RectTransform>();
            case SummonSource.デッキ:
                return BattleManager.Instance.eGraveZone.GetComponent<RectTransform>();
            default:
                return null;
        }
    }

    /// <summary>
    /// 相手の作戦を、作戦ゾーンから墓地へ送る。
    /// </summary>
    public async Task ExecuteTacticsToGraveyard(CardController tacticsCard)
    {
        if (tacticsCard == null)
            return;

        CardKind cardKind = tacticsCard.cardKind;
        int cardID = tacticsCard.cardID;
        ForgetChainTactics(tacticsCard);

        try
        {
            RectTransform tacticsSlot = BattleManager.Instance.eTacticsZone as RectTransform;
            RectTransform graveSlot = BattleManager.Instance.eGraveZone as RectTransform;
            await CardAnimationManager.Instance.AnimateDiscard(tacticsSlot, graveSlot, tacticsCard);

            if (tacticsCard != null && tacticsCard.gameObject != null)
                Destroy(tacticsCard.gameObject);

            var graveyardCard = new DeckCard(cardKind, cardID, 1);
            graveyardCard.PopulateDerivedData();
            opponentGrave.Add(graveyardCard);

            BattleManager.Instance.UpdateEnemyGraveyardUI();
            PersistentEffectManager.Instance?.RecalculateImmediately();
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"ExecuteTacticsToGraveyardでエラー: {ex.Message}");
        }
    }
}
