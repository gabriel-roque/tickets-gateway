package transaction_pix_repository

import (
	"testing"

	transaction_pix "github.com/gabriel-roque/gateway-api/internal/transaction-pix"
	transaction_interfaces "github.com/gabriel-roque/gateway-api/pkg/interfaces"
	"gorm.io/driver/sqlite"
	"gorm.io/gorm"
)

func TestSaveIsIdempotentByExternalID(t *testing.T) {
	db, err := gorm.Open(sqlite.Open("file::memory:?cache=shared"), &gorm.Config{})
	if err != nil {
		t.Fatalf("open database: %v", err)
	}
	if err := db.AutoMigrate(&transaction_pix.TransactionPix{}); err != nil {
		t.Fatalf("migrate database: %v", err)
	}

	repository := &Repository{db: db}
	first, err := repository.Save(&transaction_interfaces.CreateTransactionPix{
		Name: "original", ExternalId: "order-123", Value: 199.90,
	})
	if err != nil {
		t.Fatalf("save original: %v", err)
	}
	second, err := repository.Save(&transaction_interfaces.CreateTransactionPix{
		Name: "duplicate", ExternalId: "order-123", Value: 999.00,
	})
	if err != nil {
		t.Fatalf("save duplicate: %v", err)
	}

	if first.Id == "" || second.Id != first.Id {
		t.Fatalf("expected the persisted transaction, got first=%q second=%q", first.Id, second.Id)
	}
	if second.Name != "original" || second.Value != 199.90 {
		t.Fatalf("duplicate changed the original transaction: %#v", second)
	}
	var count int64
	if err := db.Model(&transaction_pix.TransactionPix{}).
		Where("external_id = ?", "order-123").Count(&count).Error; err != nil {
		t.Fatalf("count transactions: %v", err)
	}
	if count != 1 {
		t.Fatalf("expected one transaction, got %d", count)
	}
}
