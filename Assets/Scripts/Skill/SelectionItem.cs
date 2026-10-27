using System.Collections.Generic;

public class SelectionItem
{
    public enum SourceType { Existing, Deck };
    public SourceType source;
    public CardController existingCard;
    public DeckCard deckData;

    public static List<SelectionItem> FromCandidates(IEnumerable<CardController> existing, IEnumerable<DeckCard> deck)
    {
        var items = new List<SelectionItem>();
        if (existing != null)
        {
            foreach (var card in existing)
                items.Add(new SelectionItem { source = SourceType.Existing, existingCard = card });
        }
        if (deck != null)
        {
            foreach (var deckCard in deck)
                items.Add(new SelectionItem { source = SourceType.Deck, deckData = deckCard });
        }
        return items;
    }

    public object GetModel()
    {
        if (source == SourceType.Existing) return existingCard.model;
        return deckData;
    }

    public bool TryGetIdentity(out CardKind kind, out int id)
    {
        if (source == SourceType.Existing && existingCard != null)
        {
            kind = existingCard.cardKind;
            id = existingCard.cardID;
            return true;
        }

        if (deckData != null)
        {
            kind = deckData.カード種類;
            id = deckData.カードID;
            return true;
        }

        kind = default;
        id = 0;
        return false;
    }

    public bool IsSameCard(SelectionItem other)
    {
        if (other == null || !TryGetIdentity(out CardKind kind, out int id))
            return false;
        if (!other.TryGetIdentity(out CardKind otherKind, out int otherId))
            return false;
        return kind == otherKind && id == otherId;
    }
}
