param(
    [Parameter(Mandatory=$true)][string]$SourceTopic,
    [Parameter(Mandatory=$true)][string]$TargetTopic,
    [int]$MaxMessages = 1,
    [string]$BootstrapServer = "localhost:9092",
    [string]$KafkaContainer = "fintechpaymentsplatform-kafka-1",
    [switch]$Execute,
    [string]$Reason = "local-dlq-replay"
)

$auditPath = Join-Path $PSScriptRoot "dlq-replay-audit.log"
$timestamp = (Get-Date).ToUniversalTime().ToString("O")
"$timestamp dryRun=$(-not $Execute) source=$SourceTopic target=$TargetTopic maxMessages=$MaxMessages reason=$Reason" | Add-Content -LiteralPath $auditPath

if (-not $Execute) {
    Write-Host "Dry run only. Add -Execute to replay. Audit written to $auditPath"
    docker exec $KafkaContainer /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server $BootstrapServer --topic $SourceTopic --from-beginning --max-messages $MaxMessages --timeout-ms 5000
    exit 0
}

$messages = docker exec $KafkaContainer /opt/kafka/bin/kafka-console-consumer.sh --bootstrap-server $BootstrapServer --topic $SourceTopic --from-beginning --max-messages $MaxMessages --timeout-ms 5000
foreach ($message in $messages) {
    if ([string]::IsNullOrWhiteSpace($message)) { continue }
    $message | docker exec -i $KafkaContainer /opt/kafka/bin/kafka-console-producer.sh --bootstrap-server $BootstrapServer --topic $TargetTopic
}

Write-Host "Replay complete. The event envelope payload was forwarded unchanged; EventId is preserved. Audit written to $auditPath"