using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Deli.Setup;
using FistVR;
using UnityEngine;
using UnityEngine.SceneManagement;
using GM = FistVR.GM;

namespace Wristimate
{
    public class Behaviour : DeliBehaviour
    {
        private readonly Canvas _popup;
        private readonly TextMesh _amountText;
        private readonly TextMesh _caliberText;

        private readonly ConfigEntry<bool> _enabled;
        
        private readonly ConfigEntry<float> _totalScale;
        private readonly ConfigEntry<float> _totalOffset;
        private readonly ConfigEntry<float> _caliberScale;
        private readonly ConfigEntry<float> _caliberOffset;
        private readonly ConfigEntry<int> _fontSize;
        
        private readonly ConfigEntry<bool> _lockPitch;
        private readonly ConfigEntry<float> _viewAngle;

        private float _viewDot;

        public Behaviour()
        {
            _enabled = Config.Bind("General", "Enabled", true, "Whether or not Wristimate should do anything.");
            
            _totalScale = Config.Bind("Proportions", "TotalScale", 0.0075f, "The scale of the canvas.");
            _totalOffset = Config.Bind("Proportions", "TotalOffset", 0.075f, "The offset, relative to the center of your hand, to the canvas.");
            _caliberScale = Config.Bind("Proportions", "CaliberScale", 0.5f, "The scale, relative to the canvas, of the caliber subtext.");
            _caliberOffset = Config.Bind("Proportions", "CaliberOffset", 3f, "The downward vertical offset, relative to the remaining amount text, of the caliber description.");
            _fontSize = Config.Bind("Proportions", "FontSize", 30, "The size of the text. Increasing this but decreasing scale will result in a higher-resolution text.");

            _lockPitch = Config.Bind("Math", "LockPitch", true, "Whether or not the text should pitch up to your head.");
            _viewAngle = Config.Bind("Math", "ViewAngle", 25f, "The minimum angle (in degrees) from the magazine that you must be looking for the text to render.");

            _enabled.SettingChanged += (_, _) => EnabledUpdated();
            
            _totalScale.SettingChanged += (_, _) => TextUpdated();
            _totalOffset.SettingChanged += (_, _) => TextUpdated();
            _caliberScale.SettingChanged += (_, _) => TextUpdated();
            _caliberOffset.SettingChanged += (_, _) => TextUpdated();
            
            _fontSize.SettingChanged += (_, _) => TextUpdated();
            _viewAngle.SettingChanged += (_, _) => ConeUpdated();
            
            {
                var root = new GameObject("Display");
                root.transform.parent = transform;
                
                var amountText = new GameObject("Amount Text");
                var caliberText = new GameObject("Caliber Text");
                amountText.transform.parent = root.transform;
                caliberText.transform.parent = root.transform;

                _popup = root.AddComponent<Canvas>();
                _popup.renderMode = RenderMode.WorldSpace;
                
                _amountText = amountText.AddComponent<TextMesh>();
                _amountText.anchor = TextAnchor.UpperCenter;

                _caliberText = caliberText.AddComponent<TextMesh>();
                _caliberText.anchor = TextAnchor.UpperCenter;
                _caliberText.color = Color.grey;
            }

            TextUpdated();
            ConeUpdated();

            SceneManager.activeSceneChanged += (_, _) => Config.Reload();
        }

        private void EnabledUpdated()
        {
            if (!_enabled.Value)
            {
                _popup.gameObject.SetActive(false);
            }
        }

        private void TextUpdated()
        {
            _popup.transform.localScale = _totalScale.Value * Vector3.one;
            
            _amountText.fontSize = _fontSize.Value;
            _caliberText.fontSize = _fontSize.Value;

            var caliberTextTransform = _caliberText.transform;
            caliberTextTransform.localPosition = _caliberOffset.Value * Vector3.down;
            caliberTextTransform.localScale = _caliberScale.Value * Vector3.one;
        }

        private void ConeUpdated()
        {
            var rads = _viewAngle.Value * Mathf.Deg2Rad;

            _viewDot = Mathf.Cos(rads);
        }

        private static IEnumerable<FVRViveHand> Hands()
        {
            yield return GM.CurrentPlayerBody.LeftHand.GetComponent<FVRViveHand>();
            yield return GM.CurrentPlayerBody.RightHand.GetComponent<FVRViveHand>();
        }

        private IEnumerable<MagData> Magazines()
        {
            return Hands()
                .Select(h =>
                {
                    var trans = h.transform;
                    var interactable = h.CurrentInteractable;
                    var mag = (interactable != null ? interactable.GetComponent<FVRFireArmMagazine>() : null)!;
                    
                    return new MagData(mag, trans.position - _totalOffset.Value * trans.forward);
                })
                .Where(m => m.Magazine != null);
        }

        private void Update()
        {
            if (!_enabled.Value) return;
            
            MagData? data;
            Vector3 pos;
            {
                var head = GM.CurrentPlayerBody.Head;
                pos = head.position;
                var fwd = head.forward;

                data = Magazines()
                    .Select(d =>
                    {
                        var dir = (d.Wrist - pos).normalized;
                        var dot = Vector3.Dot(dir, fwd);

                        return new MagDataDot(d, dot);
                    })
                    .Where(x => x.Dot > _viewDot)
                    .OrderByDescending(x => x.Dot)
                    .Select(x => (MagData?) x.Data)
                    .FirstOrDefault();
            }

            if (!data.HasValue)
            {
                _popup.gameObject.SetActive(false);
                return;
            }
            
            _popup.gameObject.SetActive(true);

            {
                var mag = data.Value.Magazine;
                var wrist = data.Value.Wrist;

                _amountText.text = ((float) mag.m_numRounds / mag.m_capacity) switch
                {
                    > 1 => "WTF (> 1)",
                    > 55/60f => "Full",
                    > 45/60f => "Full~",
                    > 35/60f => "More than half",
                    > 25/60f => "About half",
                    > 15/60f => "Less than half",
                    > 5/60f => "Empty~",
                    >= 0 => "Empty",
                    < 0 => "WTF (< 0)",
                    _ => "WTF (other)"
                };

                {
                    var round = mag.LoadedRounds?.FirstOrDefault();
                    if (round != null)
                    {
                        _caliberText.gameObject.SetActive(true);
                        _caliberText.text = AM.GetFullRoundName(mag.RoundType, round.LR_Class);
                    }
                    else
                    {
                        _caliberText.gameObject.SetActive(false);
                    }
                }

                var popupTransform = _popup.transform;

                popupTransform.position = wrist;
                {
                    var offset = wrist - pos;
                    if (_lockPitch.Value)
                    {
                        offset.y = 0;
                    }

                    var dir = offset.normalized;
                    var rot = Quaternion.LookRotation(dir);
                    
                    popupTransform.rotation = rot;
                }
            }
        }
        
        private readonly struct MagDataDot
        {
            public MagDataDot(MagData data, float dot)
            {
                Data = data;
                Dot = dot;
            }
            
            public MagData Data { get; }
            public float Dot { get; }
        }

        private readonly struct MagData
        {
            public MagData(FVRFireArmMagazine magazine, Vector3 wrist)
            {
                Magazine = magazine;
                Wrist = wrist;
            }

            public FVRFireArmMagazine Magazine { get; }
            public Vector3 Wrist { get; }
        }
    }
}