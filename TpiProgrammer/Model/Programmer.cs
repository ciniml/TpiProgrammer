using System;
using System.ComponentModel;
using System.IO;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace TpiProgrammer.Model
{
    public class ProgrammingProgressInfo
    {
        public float CompletionRatio { get; private set; }
        public ProgrammingProgressInfo(float completionRatio)
        {
            this.CompletionRatio = completionRatio;
        }
    }


    public class Programmer : IDisposable
    {
        private const int FlashSectionStartAddress = 0x4000;

        private class NullProgrammingProgress : IProgress<ProgrammingProgressInfo>
        {
            public void Report(ProgrammingProgressInfo value)
            {
            }
        }

        private TpiCommunication comm;

        public Programmer(TpiCommunication comm)
        {
            this.comm = comm;
        }

        public async Task ProgramImageAsync(string path, IProgress<ProgrammingProgressInfo> progress, CancellationToken? cancellationToken)
        {
            progress = progress ?? new NullProgrammingProgress();
            cancellationToken = cancellationToken ?? CancellationToken.None;

            SparseImage<byte> image;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                image = await HexLoader.LoadAsync(stream, cancellationToken.Value);
            }
            
            await this.ProgramImageAsync(image, progress, cancellationToken);
        }
        public async Task ProgramImageAsync(SparseImage<byte> image, IProgress<ProgrammingProgressInfo> progress, CancellationToken? cancellationToken)
        {
            progress = progress ?? new NullProgrammingProgress();
            cancellationToken = cancellationToken ?? CancellationToken.None;

            await this.comm.ChipEraseAsync(cancellationToken);

            int count = 0;
            float numberOfBytes = image.Count;
            foreach (var item in image)
            {
                if ((item.Key & 1) != 0)
                {
                    continue;
                }
                if (item.Key > 0xffffu)
                {
                    break;
                }

                var lowByte = item.Value;
                var highByte = image[item.Key + 1];

                await this.comm.WordWriteAsync((ushort)(item.Key + FlashSectionStartAddress), lowByte, highByte, cancellationToken);

                // Report progress.
                progress.Report(new ProgrammingProgressInfo(count / numberOfBytes));
                count+=2;
            }
            // After programming is done, disconnect from the device.
            await this.comm.DisconnectAsync(cancellationToken);
        }
        
        public void Dispose()
        {
            if (this.comm != null)
            {
                this.comm.Dispose();
                this.comm = null;
            }
        }
    }
}