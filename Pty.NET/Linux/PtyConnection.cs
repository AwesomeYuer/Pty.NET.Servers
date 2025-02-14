﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Pty.NET.Linux
{
    using System.Diagnostics;
    using static Pty.NET.Linux.NativeMethods;

    /// <summary>
    /// A connection to a pseudoterminal on linux machines.
    /// </summary>
    internal class PtyConnection : Unix.PtyConnection
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PtyConnection"/> class.
        /// </summary>
        /// <param name="controller">The fd of the pty controller.</param>
        /// <param name="pid">The id of the spawned process.</param>
        public PtyConnection(int controller, int pid)
            : base(controller, pid)
        {
        }

        /// <inheritdoc/>
        protected override bool Kill(int controller)
        {
            return kill(this.Pid, SIGHUP) != -1;
        }

        /// <inheritdoc/>
        protected override bool Resize(int fd, int cols, int rows)
        {
            var size = new WinSize((ushort)rows, (ushort)cols);
            return ioctl(fd, TIOCSWINSZ, ref size) != -1;
        }

        /// <inheritdoc/>
        protected override bool WaitPid(int pid, ref int status)
        {
            return waitpid(pid, ref status, 0) != -1;
        }
    }
}
