using System.Collections.Generic;
using System.Reflection;
using RoR2;
using RoR2.Items;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace WhereToDie
{
    // Client side: recolours the Reincarnation icon above the N'tormat while its owner stands where the page works.
    // Green-yellow: the ritual works and nobody has to die. Green-red: the ritual works by killing you.
    //
    // "Stands where the page works" is the game's own CursedBookReadyForSacrifice buff (the host adds it and syncs it,
    // so this works with a vanilla host too). Whether the ritual kills comes from the zone the player is in.
    internal static class PageIconHighlighter
    {
        private static FieldInfo spellSlotField;
        private static FieldInfo ownerField;

        internal static void Init()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            spellSlotField = typeof(CursedBookSpellSlotIcon).GetField("spellSlot", flags);
            ownerField = typeof(CursedBookBodyBehavior.SpellSlot).GetField("owner", flags);
            if (spellSlotField == null || ownerField == null)
            {
                WhereToDiePlugin.Log.LogError("CursedBookSpellSlotIcon fields not found; the page icon highlight is disabled.");
                return;
            }
            On.RoR2.UI.CursedBookSpellSlotIcon.Update += CursedBookSpellSlotIcon_Update;
        }

        // Vanilla sets the icon's colour (white, or dark blue when spent) every frame; the gradient multiplies over it.
        private static void CursedBookSpellSlotIcon_Update(On.RoR2.UI.CursedBookSpellSlotIcon.orig_Update orig, CursedBookSpellSlotIcon self)
        {
            orig(self);

            bool? dies = WhereToDiePlugin.PageIconHighlight.Value ? State(self) : null;
            var gradient = self.GetComponent<PageIconGradient>();
            if (!gradient)
            {
                if (dies == null || !self.GetComponent<Image>())
                {
                    return;
                }
                gradient = self.gameObject.AddComponent<PageIconGradient>();
            }
            gradient.SetState(dies);

            // The page's own sprite is teal; tinting it would muddy the colours, so tint a greyscale copy instead.
            // Vanilla assigns the sprite every frame, so this swap is redone every frame too.
            var image = self.GetComponent<Image>();
            if (dies != null && image && image.sprite)
            {
                image.sprite = GreyscaleSprite(image.sprite);
            }
        }

        private static readonly Dictionary<Sprite, Sprite> greyscaleSprites = new Dictionary<Sprite, Sprite>();

        private static Sprite GreyscaleSprite(Sprite source)
        {
            if (greyscaleSprites.TryGetValue(source, out var cached))
            {
                return cached ? cached : source;
            }
            if (greyscaleSprites.ContainsValue(source))
            {
                return source;
            }
            Sprite result = null;
            try
            {
                result = MakeGreyscale(source);
            }
            catch (System.Exception e)
            {
                WhereToDiePlugin.Log.LogWarning($"Could not recolour {source.name}: {e.Message}");
            }
            greyscaleSprites[source] = result;
            return result ? result : source;
        }

        // Copies the sprite out of its (non-readable) atlas through a RenderTexture, then keeps only brightness.
        private static Sprite MakeGreyscale(Sprite source)
        {
            var texture = source.texture;
            var rect = source.textureRect;
            int width = Mathf.RoundToInt(rect.width), height = Mathf.RoundToInt(rect.height);

            var renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(texture, renderTexture);
            RenderTexture.active = renderTexture;
            var copy = new Texture2D(width, height, TextureFormat.RGBA32, false) { name = source.name + "_WhereToDie" };
            copy.ReadPixels(new Rect(rect.x, rect.y, width, height), 0, 0);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);

            var pixels = copy.GetPixels();
            float brightest = 0.001f;
            foreach (var pixel in pixels)
            {
                if (pixel.a > 0.1f)
                {
                    brightest = Mathf.Max(brightest, pixel.maxColorComponent);
                }
            }
            for (int i = 0; i < pixels.Length; i++)
            {
                float value = Mathf.Clamp01(pixels[i].maxColorComponent / brightest);
                pixels[i] = new Color(value, value, value, pixels[i].a);
            }
            copy.SetPixels(pixels);
            copy.Apply();
            Object.DontDestroyOnLoad(copy);

            var pivot = new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height);
            var sprite = Sprite.Create(copy, new Rect(0, 0, width, height), pivot, source.pixelsPerUnit);
            sprite.name = copy.name;
            Object.DontDestroyOnLoad(sprite);
            return sprite;
        }

        // null: normal icon. Otherwise whether the ritual kills.
        private static bool? State(CursedBookSpellSlotIcon icon)
        {
            var slot = spellSlotField.GetValue(icon) as CursedBookBodyBehavior.SpellSlot;
            var die = DLC4Content.CursedBookSpells.Die;
            if (slot == null || !die || slot.spellIndex != die.spellIndex || slot.isConsumed)
            {
                return null;
            }
            var owner = ownerField.GetValue(slot) as CursedBookBodyBehavior;
            var body = owner ? owner.body : null;
            if (!body || !body.HasBuff(DLC4Content.Buffs.CursedBookReadyForSacrifice))
            {
                return null;
            }
            // In a zone the client doesn't know about (only the host has it, and the host has no mod): assume the worst.
            return ZoneTracker.DiesAt(body.corePosition) ?? true;
        }
    }

    // Vertical colour gradient over a UI Graphic's vertices: green at the bottom, yellow or red at the top.
    internal class PageIconGradient : BaseMeshEffect
    {
        internal static readonly Color Green = new Color(0.35f, 1f, 0.3f, 1f);
        internal static readonly Color Yellow = new Color(1f, 0.9f, 0.2f, 1f);
        internal static readonly Color Red = new Color(1f, 0.18f, 0.12f, 1f);

        // null = no tint.
        internal bool? Dies { get; private set; }

        internal void SetState(bool? dies)
        {
            if (dies == Dies)
            {
                return;
            }
            Dies = dies;
            if (graphic)
            {
                graphic.SetVerticesDirty();
            }
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Dies == null || vh.currentVertCount == 0)
            {
                return;
            }
            var top = Dies.Value ? Red : Yellow;
            var vertices = new List<UIVertex>();
            vh.GetUIVertexStream(vertices);

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var vertex in vertices)
            {
                minY = Mathf.Min(minY, vertex.position.y);
                maxY = Mathf.Max(maxY, vertex.position.y);
            }
            float height = Mathf.Max(maxY - minY, 0.001f);

            for (int i = 0; i < vertices.Count; i++)
            {
                var vertex = vertices[i];
                // Replace the hue but keep vanilla's alpha (the icon fades when the book is idle).
                var tint = Color.Lerp(Green, top, (vertex.position.y - minY) / height);
                tint.a = vertex.color.a / 255f;
                vertex.color = tint;
                vertices[i] = vertex;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }
    }
}
