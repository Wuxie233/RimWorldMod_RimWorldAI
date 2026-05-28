using System.Text;

namespace RimWorldMCP.Compression
{
    /// <summary>
    /// RLE 压缩: 连续相同字符合并。Count 编码: 1→直接写字符(不编码), 2-20→A-S, 21-62→a-p, 63+→2位hex。
    /// 参考游戏源码: 无现有 RLE 实现，本实现为项目中首个。
    /// </summary>
    public class RleCompressor : IChunkCompressor
    {
        public string Name => "RLE";

        public string Compress(char[][] rows, (int x, int z) chunkIndex)
        {
            var sb = new StringBuilder();
            for (int r = 0; r < rows.Length; r++)
            {
                sb.Append('L');
                sb.Append(r.ToString("D2"));
                sb.Append('=');
                EncodeRleRow(rows[r], sb);
                if (r < rows.Length - 1)
                    sb.Append('\n');
            }
            return sb.ToString();
        }

        internal static void EncodeRleRow(char[] row, StringBuilder sb)
        {
            if (row.Length == 0) return;

            char current = row[0];
            int count = 1;

            for (int i = 1; i < row.Length; i++)
            {
                if (row[i] == current)
                {
                    count++;
                }
                else
                {
                    AppendRun(sb, current, count);
                    current = row[i];
                    count = 1;
                }
            }
            AppendRun(sb, current, count);
        }

        private static void AppendRun(StringBuilder sb, char c, int count)
        {
            if (count == 1)
            {
                sb.Append(c);
                return;
            }

            sb.Append(c);
            sb.Append(EncodeCount(count));
        }

        internal static string EncodeCount(int count)
        {
            if (count <= 1) return "";
            if (count <= 20) return ((char)('A' + count - 2)).ToString();    // A=2, B=3, ... S=20
            if (count <= 62) return ((char)('a' + count - 21)).ToString();   // a=21, b=22, ... p=62
            // 63+: 2位hex, 值=实际count-63
            int hex = count - 63;
            if (hex > 255) hex = 255; // 截断保护
            return hex.ToString("X2");
        }
    }
}
