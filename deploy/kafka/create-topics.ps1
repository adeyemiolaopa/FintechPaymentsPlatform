param(
    [string]$BootstrapServer = "localhost:9092",
    [int]$Partitions = 6,
    [int]$ReplicationFactor = 1,
    [int]$RetentionDays = 14,
    [string]$KafkaContainer = "fintechpaymentsplatform-kafka-1"
)

$topics = @(
    "identity.lifecycle.v1",
    "identity.lifecycle.v1.retry.1m",
    "customer.identity.lifecycle.v1.dlq",
    "customer.lifecycle.v1",
    "customer.lifecycle.v1.retry.1m",
    "account.customer.lifecycle.v1.dlq",
    "account.lifecycle.v1",
    "account.lifecycle.v1.retry.1m",
    "ledger.account.lifecycle.v1.dlq",
    "account.funds.reservation.v1",
    "account.funds.reservation.v1.retry.1m",
    "account.funds.reservation.v1.dlq",
    "account.restriction.v1",
    "account.restriction.v1.retry.1m",
    "account.restriction.v1.dlq",
    "beneficiary.lifecycle.v1",
    "beneficiary.lifecycle.v1.retry.1m",
    "beneficiary.lifecycle.v1.dlq",
    "ledger.accounts.v1",
    "ledger.accounts.v1.retry.1m",
    "ledger.accounts.v1.dlq",
    "ledger.transactions.v1",
    "ledger.transactions.v1.retry.1m",
    "ledger.transactions.v1.dlq",
    "payments.lifecycle.v1",
    "payments.lifecycle.v1.retry.1m",
    "payments.lifecycle.v1.dlq"
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