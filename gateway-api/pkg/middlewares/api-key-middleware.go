package middlewares

import (
	"net/http"
	"os"

	"github.com/gin-gonic/gin"
)

func ApiKeyMiddleware() gin.HandlerFunc {
	return func(c *gin.Context) {
		expectedApiKey := os.Getenv("X_API_KEY")

		if expectedApiKey == "" {
			c.JSON(http.StatusInternalServerError, gin.H{"error": "X_API_KEY not set"})
			c.Abort()
			return
		}

		apiKey := c.GetHeader("x-api-key")

		if apiKey != expectedApiKey {
			c.JSON(http.StatusForbidden, gin.H{"error": "Access Denied"})
			c.Abort()
			return
		}

		c.Next()
	}
}
