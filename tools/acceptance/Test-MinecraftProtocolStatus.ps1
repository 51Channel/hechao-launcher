[CmdletBinding()]
param(
    [string]$HostName = '127.0.0.1',

    [ValidateRange(1, 65535)]
    [int]$Port = 25565,

    [ValidateNotNullOrEmpty()]
    [int[]]$ProtocolVersions = @(763, 774),

    [ValidateRange(1, 30)]
    [int]$TimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'

function Write-VarInt {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Stream]$Stream,

        [Parameter(Mandatory = $true)]
        [int]$Value
    )

    [uint32]$remaining = $Value
    do {
        [byte]$next = $remaining -band 0x7f
        $remaining = $remaining -shr 7
        if ($remaining -ne 0) {
            $next = $next -bor 0x80
        }

        $Stream.WriteByte($next)
    } while ($remaining -ne 0)
}

function Read-VarInt {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Stream]$Stream
    )

    [int]$value = 0
    [int]$position = 0
    while ($position -lt 35) {
        $next = $Stream.ReadByte()
        if ($next -lt 0) {
            throw 'The remote endpoint closed while a VarInt was being read.'
        }

        $value = $value -bor (($next -band 0x7f) -shl $position)
        if (($next -band 0x80) -eq 0) {
            return $value
        }

        $position += 7
    }

    throw 'The remote endpoint returned an invalid VarInt.'
}

function Add-VarInt {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Stream]$Stream,

        [Parameter(Mandatory = $true)]
        [int]$Value
    )

    Write-VarInt -Stream $Stream -Value $Value
}

function Add-Utf8String {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Stream]$Stream,

        [Parameter(Mandatory = $true)]
        [string]$Value
    )

    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    Add-VarInt -Stream $Stream -Value $bytes.Length
    $Stream.Write($bytes, 0, $bytes.Length)
}

function Read-Exactly {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Stream]$Stream,

        [Parameter(Mandatory = $true)]
        [int]$Length
    )

    $buffer = [byte[]]::new($Length)
    $offset = 0
    while ($offset -lt $Length) {
        $read = $Stream.Read($buffer, $offset, $Length - $offset)
        if ($read -le 0) {
            throw 'The remote endpoint closed before the status packet was complete.'
        }

        $offset += $read
    }

    return $buffer
}

function Invoke-StatusProbe {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetHost,

        [Parameter(Mandatory = $true)]
        [int]$TargetPort,

        [Parameter(Mandatory = $true)]
        [int]$ProtocolVersion,

        [Parameter(Mandatory = $true)]
        [int]$Timeout
    )

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $connect = $client.ConnectAsync($TargetHost, $TargetPort)
        if (-not $connect.Wait([TimeSpan]::FromSeconds($Timeout))) {
            throw "Timed out connecting to ${TargetHost}:$TargetPort."
        }

        $stream = $client.GetStream()
        $stream.ReadTimeout = $Timeout * 1000
        $stream.WriteTimeout = $Timeout * 1000

        $handshakeBody = [IO.MemoryStream]::new()
        try {
            Add-VarInt -Stream $handshakeBody -Value 0
            Add-VarInt -Stream $handshakeBody -Value $ProtocolVersion
            Add-Utf8String -Stream $handshakeBody -Value $TargetHost
            $handshakeBody.WriteByte(($TargetPort -shr 8) -band 0xff)
            $handshakeBody.WriteByte($TargetPort -band 0xff)
            Add-VarInt -Stream $handshakeBody -Value 1

            Add-VarInt -Stream $stream -Value ([int]$handshakeBody.Length)
            $handshakeBody.Position = 0
            $handshakeBody.CopyTo($stream)
        }
        finally {
            $handshakeBody.Dispose()
        }

        # Status request: packet length 1, packet id 0.
        Add-VarInt -Stream $stream -Value 1
        Add-VarInt -Stream $stream -Value 0
        $stream.Flush()

        $packetLength = Read-VarInt -Stream $stream
        if ($packetLength -le 1 -or $packetLength -gt 1024 * 1024) {
            throw "The status packet length $packetLength is invalid."
        }

        $packet = Read-Exactly -Stream $stream -Length $packetLength
        $packetStream = [IO.MemoryStream]::new($packet, $false)
        try {
            $packetId = Read-VarInt -Stream $packetStream
            if ($packetId -ne 0) {
                throw "Expected status packet id 0, received $packetId."
            }

            $jsonLength = Read-VarInt -Stream $packetStream
            if ($jsonLength -le 0 -or $jsonLength -gt $packetStream.Length - $packetStream.Position) {
                throw "The status JSON length $jsonLength is invalid."
            }

            $jsonBytes = Read-Exactly -Stream $packetStream -Length $jsonLength
            $status = [Text.Encoding]::UTF8.GetString($jsonBytes) | ConvertFrom-Json
        }
        finally {
            $packetStream.Dispose()
        }

        if ($null -eq $status.version -or
            [string]::IsNullOrWhiteSpace([string]$status.version.name) -or
            [int]$status.version.protocol -le 0) {
            throw 'The response does not contain valid Minecraft version metadata.'
        }

        [pscustomobject]@{
            Target = "${TargetHost}:$TargetPort"
            RequestedProtocol = $ProtocolVersion
            ResponseVersion = [string]$status.version.name
            ResponseProtocol = [int]$status.version.protocol
            OnlinePlayers = [int]$status.players.online
            MaximumPlayers = [int]$status.players.max
            Passed = $true
        }
    }
    finally {
        $client.Dispose()
    }
}

$results = foreach ($protocolVersion in $ProtocolVersions) {
    Invoke-StatusProbe `
        -TargetHost $HostName `
        -TargetPort $Port `
        -ProtocolVersion $protocolVersion `
        -Timeout $TimeoutSeconds
}

$results | ConvertTo-Json -Depth 3
