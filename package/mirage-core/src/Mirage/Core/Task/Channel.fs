module Mirage.Core.Task.Channel

open System.Threading
open System.Threading.Channels

type Channel<'A> =
    private
        {   writer: ChannelWriter<'A>
            reader: ChannelReader<'A>
            cancellationToken: CancellationToken
        }

let Channel cancellationToken =
    let channel = Channels.Channel.CreateUnbounded()
    {
        writer = channel.Writer
        reader = channel.Reader
        cancellationToken = cancellationToken
    }

let writeChannel channel element = ignore <| channel.writer.WriteAsync(element, channel.cancellationToken)

let readChannel channel = channel.reader.ReadAsync channel.cancellationToken