// e:\git\RimTalkTTSModule\Source\UI\Player2VoicesWindow.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimTalk.TTS.Data;
using RimTalk.TTS.Service;
using UnityEngine;
using Verse;

namespace RimTalk.TTS.UI
{
    /// <summary>
    /// Window listing Player2 voices with add-to-config and preview actions
    /// </summary>
    public class Player2VoicesWindow : Window
    {
        private readonly TTSSettings settings;
        private List<Player2TTSClient.VoiceInfo> voices;
        private Vector2 scrollPos = Vector2.zero;
        private bool loading;
        private string searchText = "";
        private string previewingId = "";
        private readonly ConcurrentQueue<Action> mainThreadActions = new ConcurrentQueue<Action>();

        public Player2VoicesWindow(TTSSettings settings)
        {
            this.settings = settings;
            this.doCloseX = true;
            this.forcePause = false;
            this.absorbInputAroundWindow = true;
            LoadVoices();
        }

        public override Vector2 InitialSize => new Vector2(1000f, 700f);

        private void Enqueue(Action a) => mainThreadActions.Enqueue(a);

        private void LoadVoices()
        {
            loading = true;
            voices = null;
            string baseUrl = settings.GetSupplierBaseUrl(TTSSettings.TTSSupplier.Player2TTS);

            Task.Run(async () =>
            {
                var list = await Player2TTSClient.GetVoicesAsync(baseUrl);
                Enqueue(() =>
                {
                    voices = list;
                    loading = false;
                });
            });
        }

        private void AddVoice(Player2TTSClient.VoiceInfo voice)
        {
            var models = settings.GetSupplierVoiceModels(TTSSettings.TTSSupplier.Player2TTS);
            if (models.Any(m => m.ModelId == voice.id)) return;
            models.Add(new VoiceModel(voice.id, $"{voice.name} ({voice.language})"));
            settings.SetSupplierVoiceModels(TTSSettings.TTSSupplier.Player2TTS, models);
        }

        private void PreviewVoice(Player2TTSClient.VoiceInfo voice)
        {
            if (previewingId == voice.id) return;
            previewingId = voice.id;

            string baseUrl = settings.GetSupplierBaseUrl(TTSSettings.TTSSupplier.Player2TTS);
            string apiKey = settings.GetSupplierApiKey(TTSSettings.TTSSupplier.Player2TTS);
            var request = new TTSRequest
            {
                ApiKey = apiKey,
                Voice = voice.id,
                Input = Player2TTSClient.GetPreviewText(voice.language),
                Speed = 1f
            };

            Task.Run(async () =>
            {
                byte[] data = await Player2TTSClient.GenerateSpeechAsync(request, baseUrl);
                Enqueue(() =>
                {
                    previewingId = "";
                    if (data != null && data.Length > 0)
                        _ = AudioPlaybackService.PlayPreviewAsync(data);
                });
            });
        }

        public override void DoWindowContents(Rect inRect)
        {
            while (mainThreadActions.TryDequeue(out var action))
                action?.Invoke();

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 120f, 35f), "RimTalk.Settings.TTS.Player2.WindowTitle".Translate());
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(new Rect(inRect.width - 110f, 2f, 110f, 30f), "RimTalk.Settings.TTS.Player2.Refresh".Translate()))
                LoadVoices();

            float topY = 42f;
            Widgets.Label(new Rect(0f, topY, 60f, 28f), "RimTalk.Settings.TTS.Player2.Search".Translate());
            searchText = Widgets.TextField(new Rect(62f, topY, 240f, 28f), searchText ?? "");

            float listTop = topY + 36f;
            Rect headerRect = new Rect(0f, listTop, inRect.width - 16f, 28f);

            float btnW = 80f;
            float genderW = 70f;
            float langW = 180f;
            float nameW = headerRect.width - btnW * 2 - 8f - genderW - langW;

            Widgets.DrawBoxSolid(headerRect, new Color(0.25f, 0.25f, 0.25f));
            float hx = headerRect.x;
            Widgets.Label(new Rect(hx + 5f, headerRect.y + 5f, nameW, 25f), "RimTalk.Settings.TTS.Player2.VoiceName".Translate());
            hx += nameW;
            Widgets.Label(new Rect(hx + 5f, headerRect.y + 5f, langW, 25f), "RimTalk.Settings.TTS.Player2.Language".Translate());
            hx += langW;
            Widgets.Label(new Rect(hx + 5f, headerRect.y + 5f, genderW, 25f), "RimTalk.Settings.TTS.Player2.Gender".Translate());

            Rect listRect = new Rect(0f, listTop + 28f, inRect.width, inRect.height - listTop - 28f);

            if (loading)
            {
                Widgets.Label(new Rect(0f, listRect.y + 10f, inRect.width, 30f), "RimTalk.Settings.TTS.Player2.Loading".Translate());
                return;
            }

            if (voices == null)
            {
                Widgets.Label(new Rect(0f, listRect.y + 10f, inRect.width, 30f), "RimTalk.Settings.TTS.Player2.LoadFailed".Translate());
                return;
            }

            IEnumerable<Player2TTSClient.VoiceInfo> filtered = voices;
            if (!string.IsNullOrWhiteSpace(searchText))
            {
                string q = searchText.Trim().ToLower();
                filtered = filtered.Where(v =>
                    (v.name != null && v.name.ToLower().Contains(q)) ||
                    (v.language != null && v.language.ToLower().Contains(q)));
            }

            var ordered = filtered.OrderBy(v => v.language).ThenBy(v => v.name).ToList();
            if (ordered.Count == 0)
            {
                Widgets.Label(new Rect(0f, listRect.y + 10f, inRect.width, 30f), "RimTalk.Settings.TTS.Player2.Empty".Translate());
                return;
            }

            float rowH = 32f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, ordered.Count * rowH);
            Widgets.BeginScrollView(listRect, ref scrollPos, viewRect);

            var models = settings.GetSupplierVoiceModels(TTSSettings.TTSSupplier.Player2TTS);

            for (int i = 0; i < ordered.Count; i++)
            {
                var v = ordered[i];
                Rect row = new Rect(0f, i * rowH, viewRect.width, rowH);
                if (i % 2 == 0)
                    Widgets.DrawAltRect(row);

                float cx = row.x;
                Widgets.Label(new Rect(cx + 5f, row.y + 6f, nameW, 25f), v.name);
                cx += nameW;
                Widgets.Label(new Rect(cx + 5f, row.y + 6f, langW, 25f), v.language);
                cx += langW;
                Widgets.Label(new Rect(cx + 5f, row.y + 6f, genderW, 25f), v.gender);
                cx += genderW;

                bool added = models.Any(m => m.ModelId == v.id);
                Rect addRect = new Rect(cx + 4f, row.y + 2f, btnW, 28f);
                if (added)
                {
                    GUI.enabled = false;
                    Widgets.ButtonText(addRect, "RimTalk.Settings.TTS.Player2.Added".Translate());
                    GUI.enabled = true;
                }
                else if (Widgets.ButtonText(addRect, "RimTalk.Settings.TTS.Player2.Add".Translate()))
                {
                    AddVoice(v);
                }

                bool previewing = previewingId == v.id;
                Rect playRect = new Rect(cx + btnW + 8f, row.y + 2f, btnW, 28f);
                GUI.enabled = !previewing;
                if (Widgets.ButtonText(playRect, previewing ? "…" : "RimTalk.Settings.TTS.Player2.Preview".Translate()))
                {
                    PreviewVoice(v);
                }
                GUI.enabled = true;
            }

            Widgets.EndScrollView();
        }
    }
}
