using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Reads a model's attachment points (where WoW hangs helms, weapons, shields, shoulders)
    /// straight from its raw .m2 — and, for modern character models that keep their skeleton
    /// apart, its .skel. wow.export's glTF export leaves them out, so a helm comes out at the
    /// feet with nothing to say where the head's attachment is. Layouts follow wow.export's own
    /// readers (src/app.js, parseChunk_MD21_attachments and SKELLoader.parse_chunk_ska1).
    ///
    /// In the rest pose an attached item is simply its mesh moved by the attachment's position
    /// (getAttachmentTransform: the bone's matrix, identity at rest, times a translation).
    /// </summary>
    public static class M2Attachments
    {
        // Attachment IDs (wow.export's ATTACHMENT_ID).
        public const int Shield = 0;
        public const int HandRight = 1;
        public const int HandLeft = 2;
        public const int ShoulderRight = 5;
        public const int ShoulderLeft = 6;
        public const int Helmet = 11;
        public const int Back = 12;
        // Where weapons and shields go when put away: across the back, and on the hips.
        public const int SheathMainHand = 26;
        public const int SheathOffHand = 27;
        public const int SheathShield = 28;
        public const int HipWeaponLeft = 32;
        public const int HipWeaponRight = 33;

        public struct Attachment
        {
            public int id;
            /// <summary>Index into the model's bones — the glTF skin's joint order.</summary>
            public int bone;
            /// <summary>In the model's space at rest, already in glTF axes (x, z, −y of the file).</summary>
            public Vector3 gltfPosition;
        }

        // Offset of the attachments M2Array in the MD20 header (and the lookup right after it).
        const int M2AttachmentsOffset = 0xF0;
        // id (4) + bone (2) + unknown (2) + position (12) + animateAttached M2Track (20).
        const int AttachmentStride = 40;

        /// <summary>
        /// Attachments by ID, from the .m2 and then the .skel for any the .m2 doesn't have.
        /// Either path may be null.
        /// </summary>
        public static Dictionary<int, Attachment> Read(string m2Path, string skelPath)
        {
            var result = new Dictionary<int, Attachment>();
            if (m2Path != null && File.Exists(m2Path))
            {
                var data = File.ReadAllBytes(m2Path);
                int md20 = Ascii(data, 0) == "MD20" ? 0 : FindChunk(data, "MD21");
                if (md20 >= 0)
                    ReadArrays(data, md20, md20 + M2AttachmentsOffset, result);
            }
            if (skelPath != null && File.Exists(skelPath))
            {
                var data = File.ReadAllBytes(skelPath);
                int ska1 = FindChunk(data, "SKA1");
                if (ska1 >= 0)
                    ReadArrays(data, ska1, ska1, result);
            }
            return result;
        }

        /// <summary>
        /// The attachment array and its ID lookup sit back to back at <paramref name="header"/>;
        /// their offsets count from <paramref name="origin"/>.
        /// </summary>
        static void ReadArrays(byte[] data, int origin, int header, Dictionary<int, Attachment> into)
        {
            if (header + 16 > data.Length)
                return;
            int count = (int)U32(data, header);
            int offset = origin + (int)U32(data, header + 4);
            int lookupCount = (int)U32(data, header + 8);
            int lookupOffset = origin + (int)U32(data, header + 12);
            if (count <= 0 || offset + count * AttachmentStride > data.Length ||
                lookupOffset + lookupCount * 2 > data.Length)
                return;

            for (int id = 0; id < lookupCount; id++)
            {
                int index = (short)(data[lookupOffset + id * 2] | data[lookupOffset + id * 2 + 1] << 8);
                if (index < 0 || index >= count || into.ContainsKey(id))
                    continue;
                int at = offset + index * AttachmentStride;
                float x = F32(data, at + 8), y = F32(data, at + 12), z = F32(data, at + 16);
                into[id] = new Attachment
                {
                    id = (int)U32(data, at),
                    bone = data[at + 4] | data[at + 5] << 8,
                    gltfPosition = new Vector3(x, z, -y),
                };
            }
        }

        /// <summary>Start of the named chunk's data in a chunked file, or −1.</summary>
        static int FindChunk(byte[] data, string magic)
        {
            int at = 0;
            while (at + 8 <= data.Length)
            {
                string name = Ascii(data, at);
                int size = (int)U32(data, at + 4);
                if (name == magic)
                    return at + 8;
                if (size < 0)
                    return -1;
                at += 8 + size;
            }
            return -1;
        }

        static string Ascii(byte[] data, int at) => at + 4 <= data.Length ? Encoding.ASCII.GetString(data, at, 4) : "";
        static uint U32(byte[] data, int at) => (uint)(data[at] | data[at + 1] << 8 | data[at + 2] << 16 | data[at + 3] << 24);
        static float F32(byte[] data, int at) => System.BitConverter.ToSingle(data, at);
    }
}
