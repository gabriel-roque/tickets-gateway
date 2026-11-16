package database

import (
	"testing"

	transaction_pix "github.com/gabriel-roque/gateway-api/internal/transaction-pix"
	"gorm.io/driver/sqlite"
	"gorm.io/gorm"
)

func TestMigrationUpgradesOldSchemaAndDeduplicatesExternalID(t *testing.T) {
	db, err := gorm.Open(sqlite.Open(":memory:"), &gorm.Config{})
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	if err := db.Exec(`CREATE TABLE transaction_pix (
		id text PRIMARY KEY, name text NOT NULL, external_id text NOT NULL,
		value real NOT NULL, qr_code text NOT NULL, status numeric NOT NULL
	)`).Error; err != nil {
		t.Fatalf("create old schema: %v", err)
	}
	for _, id := range []string{"00000000-0000-4000-8000-000000000001", "00000000-0000-4000-8000-000000000002"} {
		if err := db.Exec(`INSERT INTO transaction_pix
			(id, name, external_id, value, qr_code, status) VALUES (?, 'name', 'order-1', 10, 'qr', 0)`, id).Error; err != nil {
			t.Fatalf("seed duplicate: %v", err)
		}
	}

	if err := migrateConnection(db); err != nil {
		t.Fatalf("migrate old schema: %v", err)
	}

	var count int64
	if err := db.Model(&transaction_pix.TransactionPix{}).Where("external_id = ?", "order-1").Count(&count).Error; err != nil {
		t.Fatalf("count migrated rows: %v", err)
	}
	if count != 1 {
		t.Fatalf("expected one surviving row, got %d", count)
	}
	duplicate := transaction_pix.TransactionPix{
		Id: "00000000-0000-4000-8000-000000000003", Name: "duplicate",
		ExternalId: "order-1", Value: 10, QrCode: "qr", Status: false,
	}
	if err := db.Create(&duplicate).Error; err == nil {
		t.Fatal("expected unique external_id index to reject a duplicate")
	}
}
