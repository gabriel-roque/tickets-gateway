package transaction_pix_handlers

import (
	transaction_pix "github.com/gabriel-roque/gateway-api/internal/transaction-pix"
	interfaces "github.com/gabriel-roque/gateway-api/pkg/interfaces"
)

func (u *TransactionPixHandler) Create(transaction interfaces.CreateTransactionPix) transaction_pix.TransactionPix {
	return u.repository.Save(&transaction)
}
