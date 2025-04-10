package transaction_pix_interfaces

import (
	transaction_pix "github.com/gabriel-roque/gateway-api/internal/transaction-pix"
	interfaces "github.com/gabriel-roque/gateway-api/pkg/interfaces"
)

type ITransactionPixRepository interface {
	Save(transaction *interfaces.CreateTransactionPix) transaction_pix.TransactionPix
	GetById(id string) (transaction_pix.TransactionPix, error)
}
