using UnityEngine;
using UnityEngine.InputSystem;

namespace Zcom
{
    public struct PlayerInputFrame
    {
        public Vector2 move;
        public Vector2 pointer;   // screen pixels
        public bool fire;         // held
        public bool reload;       // pressed this frame
        public bool dash;         // pressed this frame
        public bool grenade;      // held (release throws)
        public bool restart;      // pressed this frame
        public bool interact;     // pressed this frame (E)
        public bool inventory;    // pressed this frame (Tab / I)
        public int slot;          // weapon slot pressed this frame (0-based), -1 = none
        public int cycle;         // +1 / -1 from the mouse wheel, 0 = none
        // UI (inventory)
        public bool click;        // left button pressed this frame
        public bool clickUp;      // left button released this frame
        public bool rightClick;   // right button pressed this frame
        public bool shift;        // held
        public bool takeAll;      // F pressed this frame
        public bool cancel;       // Esc pressed this frame
        public bool help;         // H pressed this frame (controls overlay)
        public bool scan;         // Q pressed this frame (scan pulse ability)
    }

    /// <summary>
    /// Single place that reads devices. Read once per frame and cached so every system sees the same input.
    /// <see cref="Override"/> lets tests / autopilot drive the player.
    /// </summary>
    public static class PlayerInputSource
    {
        public static System.Func<PlayerInputFrame> Override;

        static int cachedFrame = -1;
        static PlayerInputFrame cached;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { cachedFrame = -1; }

        public static PlayerInputFrame Current
        {
            get
            {
                if (cachedFrame != Time.frameCount)
                {
                    cachedFrame = Time.frameCount;
                    cached = Override != null ? Override() : ReadDevices();
                }
                return cached;
            }
        }

        static PlayerInputFrame ReadDevices()
        {
            var f = new PlayerInputFrame { slot = -1 };
            var k = Keyboard.current;
            var m = Mouse.current;
            if (k != null)
            {
                float x = 0, y = 0;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) x -= 1;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) x += 1;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) y -= 1;
                if (k.wKey.isPressed || k.upArrowKey.isPressed) y += 1;
                f.move = Vector2.ClampMagnitude(new Vector2(x, y), 1f);
                f.reload = k.rKey.wasPressedThisFrame;
                f.dash = k.spaceKey.wasPressedThisFrame || k.leftShiftKey.wasPressedThisFrame;
                f.grenade = k.gKey.isPressed;
                f.scan = k.qKey.wasPressedThisFrame;
                f.restart = k.rKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame;
                f.interact = k.eKey.wasPressedThisFrame;
                f.inventory = k.tabKey.wasPressedThisFrame || k.iKey.wasPressedThisFrame;
                if (k.digit1Key.wasPressedThisFrame) f.slot = 0;
                else if (k.digit2Key.wasPressedThisFrame) f.slot = 1;
                else if (k.digit3Key.wasPressedThisFrame) f.slot = 2;
                f.shift = k.leftShiftKey.isPressed || k.rightShiftKey.isPressed;
                f.takeAll = k.fKey.wasPressedThisFrame;
                f.cancel = k.escapeKey.wasPressedThisFrame;
                f.help = k.hKey.wasPressedThisFrame;
            }
            if (m != null)
            {
                f.pointer = m.position.ReadValue();
                f.fire = m.leftButton.isPressed;
                f.click = m.leftButton.wasPressedThisFrame;
                f.clickUp = m.leftButton.wasReleasedThisFrame;
                f.rightClick = m.rightButton.wasPressedThisFrame;
                f.grenade |= m.rightButton.isPressed;
                float wheel = m.scroll.ReadValue().y;
                f.cycle = wheel > 0.01f ? -1 : wheel < -0.01f ? 1 : 0;
            }
            return f;
        }
    }
}
