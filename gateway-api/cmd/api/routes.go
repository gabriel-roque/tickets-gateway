package main

import (
	controllers "github.com/gabriel-roque/tickets-gateway/cmd/api/controllers/transaction"
	"github.com/gin-gonic/gin"
)

// TODO: Middleware X-API-KEY
func TransactionPixRoutes(router *gin.Engine) {
	transactionPixRoutes := router.Group("/transaction-pix")

	transactionPixRoutes.POST("/", controllers.CreateTransactionPix)
	transactionPixRoutes.GET("/:id", controllers.GetTransactionPix)
}
