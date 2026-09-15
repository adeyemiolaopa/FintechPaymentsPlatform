param(
    [string]$BootstrapServer = "localhost:9092",
    [int]$Partitions = 6,
    [int]$ReplicationFactor = 1,
    [int]$RetentionDays = 14,
    [string]$KafkaContainer = "fintechpaymentsplatform-kafka-1"
)

$topics = @(
    "identity.lifecycle.v1",
    "customer.lifecycle.v1",
    "account.lifecycle.v1",
    "account.funds.reservation.v1",
    "account.restriction.v1",
    "beneficiary.lifecycle.v1",
    "ledger.accounts.v1",
    "ledger.transactions.v1",
    "payments.lifecycle.v1"
)

$retentionMs = [int64]$RetentionDays * 24 * 60 * 60 * 1000
foreach ($topic in $topics) {
    docker exec $KafkaContainer /opt/kafka/bin/kafka-topics.sh `
        --bootstrap-server $BootstrapServer `
        --create `
        --if-not-exists `
        --topic $topic `
        --partitions $Partitions `
        --replication-factor $ReplicationFactor `
        --config retention.ms=$retentionMs
}