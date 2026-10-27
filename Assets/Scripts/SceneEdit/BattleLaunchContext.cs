using System.Collections.Generic;
using UnityEngine.SceneManagement;

/// <summary>
/// デッキ編集から始めるソリティア対戦の、シーンをまたぐ設定。
/// 通常のタイトルからの対戦では使わない。
/// </summary>
public static class BattleLaunchContext
{
    public const string TitleSceneName = "TitleScene";
    public const string DeckEditSceneName = "DeckEditScene";

    static string returnSceneName = TitleSceneName;
    static bool forceFirstPlayer;
    static bool disableDeckShuffle;
    static List<DeckCard> playerDeck;
    static List<DeckCard> enemyDeck;

    public static bool ForceFirstPlayer => forceFirstPlayer;
    public static bool DisableDeckShuffle => disableDeckShuffle;
    public static bool ReturnsToDeckEdit => returnSceneName == DeckEditSceneName;

    public static void BeginSolitaire(List<DeckCard> deck, bool disableShuffle)
    {
        returnSceneName = DeckEditSceneName;
        forceFirstPlayer = true;
        disableDeckShuffle = disableShuffle;
        playerDeck = CopyDeck(deck);
        enemyDeck = CopyDeck(deck);
    }

    public static void ClearBattleSettings()
    {
        returnSceneName = TitleSceneName;
        forceFirstPlayer = false;
        disableDeckShuffle = false;
        playerDeck = null;
        enemyDeck = null;
    }

    public static bool TryCopyPlayerDeck(out List<DeckCard> deck)
    {
        if (playerDeck == null)
        {
            deck = null;
            return false;
        }

        deck = CopyDeck(playerDeck);
        return true;
    }

    public static bool TryCopyEnemyDeck(out List<DeckCard> deck)
    {
        if (enemyDeck == null)
        {
            deck = null;
            return false;
        }

        deck = CopyDeck(enemyDeck);
        return true;
    }

    public static string ConsumeReturnScene()
    {
        string scene = string.IsNullOrEmpty(returnSceneName) ? TitleSceneName : returnSceneName;
        ClearBattleSettings();
        return scene;
    }

    public static void LoadReturnScene()
    {
        SceneManager.LoadScene(ConsumeReturnScene());
    }

    static List<DeckCard> CopyDeck(List<DeckCard> source)
    {
        var copy = new List<DeckCard>();
        if (source == null)
            return copy;

        foreach (DeckCard card in source)
        {
            if (card == null)
                continue;

            List<CardCategoryList> category = card.カテゴリ != null
                ? new List<CardCategoryList>(card.カテゴリ)
                : new List<CardCategoryList>();
            copy.Add(new DeckCard(card.カード種類, card.カードID, card.コスト, category, card.カードタイプ));
        }

        return copy;
    }
}
