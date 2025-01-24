package transaction_pix_handlers

import (
	interfaces "github.com/gabriel-roque/tickets-gateway/pkg/interfaces"
)

func (u *TransactionPixHandler) Create(transaction interfaces.CreateTransactionPix) {
	u.repository.Save(&transaction)
}
