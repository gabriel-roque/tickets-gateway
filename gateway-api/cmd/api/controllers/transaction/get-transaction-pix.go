package controllers

import (
	"net/http"

	transaction_pix_handlers "github.com/gabriel-roque/tickets-gateway/internal/transaction-pix/handlers"
	"github.com/gin-gonic/gin"
)

func GetTransactionPix(ctx *gin.Context) {
	id := ctx.Param("id")

	handler := transaction_pix_handlers.NewTransactionPixHandler()
	transaction, err := handler.GetById(id)

	if err != nil {
		ctx.Status(http.StatusNotFound)
		return
	}

	ctx.JSON(http.StatusOK, transaction)
}
